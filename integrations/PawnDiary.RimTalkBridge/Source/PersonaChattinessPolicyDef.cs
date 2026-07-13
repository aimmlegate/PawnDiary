// XML boundary for Pawn Diary -> RimTalk chattiness inference. The Def owns tunable baselines and
// variation; the actual selection/hash math remains in the dependency-free pure helper.
using System;
using System.Collections.Generic;
using Verse;

namespace PawnDiaryRimTalkBridge
{
    /// <summary>Per-psychotype RimTalk talk-initiation weights with stable pawn variation.</summary>
    public sealed class PersonaChattinessPolicyDef : Def
    {
        public const string PolicyDefName = "RimTalk_PersonaChattiness";

        /// <summary>Fallback for Neutral, custom, or future unknown psychotypes.</summary>
        public float defaultBaseline = 0.5f;

        /// <summary>Relative stable variation around the baseline; 0.15 means ±15%.</summary>
        public float variationFraction = 0.15f;

        /// <summary>Exact per-psychotype baseline catalog loaded from XML.</summary>
        public List<Pure.PersonaChattinessProfile> profiles =
            new List<Pure.PersonaChattinessProfile>();

        private static readonly PersonaChattinessPolicyDef Fallback =
            new PersonaChattinessPolicyDef { defName = PolicyDefName };

        /// <summary>Returns loaded XML policy or safe neutral defaults if the Def is unavailable.</summary>
        public static PersonaChattinessPolicyDef Current
        {
            get
            {
                return DefDatabase<PersonaChattinessPolicyDef>.GetNamedSilentFail(PolicyDefName) ?? Fallback;
            }
        }

        /// <summary>Resolves one stable, clamped talk-initiation weight.</summary>
        public float ChattinessFor(string pawnStableId, string psychotypeDefName)
        {
            return Pure.PersonaChattinessPolicy.Resolve(
                pawnStableId, psychotypeDefName, profiles, defaultBaseline, variationFraction);
        }

        /// <summary>Reports invalid/duplicate tuning during Def loading.</summary>
        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors()) yield return error;
            if (defaultBaseline < 0f || defaultBaseline > 1f)
            {
                yield return "defaultBaseline must be in [0,1].";
            }
            if (variationFraction < 0f || variationFraction > 1f)
            {
                yield return "variationFraction must be in [0,1].";
            }

            HashSet<string> names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (profiles == null) yield break;
            for (int i = 0; i < profiles.Count; i++)
            {
                Pure.PersonaChattinessProfile profile = profiles[i];
                if (profile == null || string.IsNullOrWhiteSpace(profile.psychotypeDefName))
                {
                    yield return "every chattiness profile requires psychotypeDefName.";
                    continue;
                }
                if (!names.Add(profile.psychotypeDefName.Trim()))
                {
                    yield return "duplicate chattiness profile: " + profile.psychotypeDefName;
                }
                if (profile.baseline < 0f || profile.baseline > 1f)
                {
                    yield return profile.psychotypeDefName + " baseline must be in [0,1].";
                }
            }
        }
    }
}
