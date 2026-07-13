// XML-owned tuning for the Rimpsyche bridge.
//
// Thresholds and caps are policy, so AGENTS.md keeps them in Def XML instead of scattering magic
// numbers through the Harmony hook and prompt-summary code. The static Current accessor is the one
// impure edge that reads Verse's DefDatabase; pure helpers receive the resolved primitive values.
//
// New to C#/RimWorld? See AGENTS.md ("XML Defs") and docs/lore/defs.md.
using Verse;

namespace PawnDiaryRimpsyche
{
    /// <summary>Adapter-local thresholds/caps loaded from DiaryExternalGroups_Rimpsyche.xml.</summary>
    public class RimpsycheBridgeTuningDef : Def
    {
        // These code values are defensive fallbacks only. The shipped policy lives in XML.
        public float summaryMagnitudeFloor = 0.35f;
        public int summaryMaxDescriptors = 3;
        public int summaryMaxInterests = 2;
        public float conversationAlignmentThreshold = 0.55f;
        public int conversationPairCooldownTicks = 60000;

        /// <summary>Maximum completion tokens for one persona rewrite request.</summary>
        public int transformMaxTokens = 220;

        private static RimpsycheBridgeTuningDef fallback;

        /// <summary>
        /// Returns the shipped XML Def, or a safe fallback if another mod removed/broke it. Never uses
        /// GetNamed (which would throw during partial or unusual Def loads).
        /// </summary>
        public static RimpsycheBridgeTuningDef Current
        {
            get
            {
                RimpsycheBridgeTuningDef configured =
                    DefDatabase<RimpsycheBridgeTuningDef>.GetNamedSilentFail(BridgeIds.TuningDefName);
                if (configured != null)
                {
                    return configured;
                }

                if (fallback == null)
                {
                    fallback = new RimpsycheBridgeTuningDef();
                }

                return fallback;
            }
        }
    }
}
