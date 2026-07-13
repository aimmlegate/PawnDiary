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
        private const float SafeDefaultBaseline = 0.5f;
        private const float SafeVariationFraction = 0.15f;

        /// <summary>Returns a stable clamped baseline × relative pawn variation.</summary>
        public static float Resolve(string pawnStableId, string psychotypeDefName,
            IList<PersonaChattinessProfile> profiles, float defaultBaseline, float variationFraction)
        {
            float baseline = IsFinite01(defaultBaseline) ? defaultBaseline : SafeDefaultBaseline;
            string wantedPsychotype = (psychotypeDefName ?? string.Empty).Trim();
            if (profiles != null)
            {
                for (int i = 0; i < profiles.Count; i++)
                {
                    PersonaChattinessProfile profile = profiles[i];
                    string profileName = profile == null
                        ? string.Empty
                        : (profile.psychotypeDefName ?? string.Empty).Trim();
                    if (profile != null && string.Equals(profileName,
                            wantedPsychotype, StringComparison.OrdinalIgnoreCase))
                    {
                        if (IsFinite01(profile.baseline)) baseline = profile.baseline;
                        break;
                    }
                }
            }

            float spread = IsFinite01(variationFraction) ? variationFraction : SafeVariationFraction;
            if (spread <= 0f || baseline <= 0f)
            {
                return baseline;
            }

            // FNV-1a is stable across Mono/processes, unlike string.GetHashCode. Normalize case so a
            // defensive case-insensitive Def match cannot produce a different variation.
            uint hash = 2166136261u;
            HashText(ref hash, pawnStableId);
            HashChar(ref hash, '|');
            HashText(ref hash, wantedPsychotype);
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

        private static bool IsFinite01(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value) && value >= 0f && value <= 1f;
        }
    }
}
