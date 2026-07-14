// XML boundary for the psychotype roll's numeric tuning. RimWorld loads the Def below at startup; this
// file copies it into the plain PsychotypeRollWeights DTO so the pure roll algorithm never depends on
// Verse or live Def objects. This is the sibling of DiaryPsychotypeTraitPolicyDef (which owns the trait
// half of the same roll): same shape — a single policy Def, a GetNamedSilentFail lookup with a safe
// fallback, and a Snapshot() that copies each XML field across with a defensive clamp where it matters.
//
// The values mirror design/PSYCHOTYPE_PLAN.md "Tuning knobs". They are odds/weights/thresholds and so
// belong in XML per AGENTS.md rule #3; before this they were compile-time constants in
// PsychotypeRollPolicy.cs. The combo-signature count thresholds stay in the policy (see the plan's
// "Out of scope") because they are structural matching rules entangled with their signatures.
using System;
using Verse;

namespace PawnDiary
{
    /// <summary>
    /// Single XML-owned policy Def for the numeric tuning of a psychotype roll (family bases, bonuses,
    /// wildcard chance, jitter range, duplicate penalty). Each field defaults to the shipped value, so
    /// the roll behaves identically when the Def is missing.
    /// </summary>
    public class DiaryPsychotypeRollPolicyDef : Def
    {
        // Family bases: grounded is the broad default, the three skewed families start rarer.
        public float familyBaseGrounded = 6f;
        public float familyBaseSkewed = 2f;
        // Inward leans for a pawn with no passions at all and for creepjoiners.
        public float zeroPassionInwardBonus = 4f;
        public float creepjoinerInwardBonus = 4f;
        // Grounded picks up a small "settled" nudge when the pawn has passions but none burning.
        public float groundedNoBurningBonus = 1f;
        // Intense leans when several passions burn at once; anxious leans when one domain dominates.
        public int burningIntenseThreshold = 3;
        public float burningIntenseBonus = 2f;
        public float focusThreshold = 2f / 3f;
        public int focusMinTotalPoints = 3;
        public float focusAnxiousBonus = 2f;
        // Stage-2 member weighting.
        public float memberBaseWeight = 1f;
        public float comboBonus = 2f;
        public float continuityBonus = 1f;
        // Wildcard branch: skip all profile logic this often and roll flat over the stage candidates.
        public float wildcardChance = 0.12f;
        public float wildcardGroundedBase = 2f;
        public float wildcardSkewedBase = 1f;
        // Per-candidate jitter multiplier range and the soft duplicate penalty applied per existing holder.
        public float jitterMin = 0.8f;
        public float jitterMax = 1.3f;
        public float duplicatePenalty = 0.25f;
    }

    /// <summary>Finds the policy Def and safely projects it into the pure roll contract.</summary>
    internal static class DiaryPsychotypeRollPolicy
    {
        private const string DefName = "Diary_PsychotypeRollPolicy";

        /// <summary>
        /// Returns a fresh snapshot so settings/reloads cannot mutate an in-progress roll. When the Def
        /// is absent or a field is left unset, each PsychotypeRollWeights default reproduces the shipped
        /// value, so a missing or partial override is always a safe no-op.
        /// </summary>
        public static PsychotypeRollWeights Snapshot()
        {
            DiaryPsychotypeRollPolicyDef source =
                DefDatabase<DiaryPsychotypeRollPolicyDef>.GetNamedSilentFail(DefName);
            PsychotypeRollWeights snapshot = new PsychotypeRollWeights();
            if (source == null)
            {
                return snapshot;
            }

            // Probabilities are clamped to [0,1]; a bad jitter range is ordered so min<=max; everything
            // else is copied verbatim. The pure algorithm floors every weight at WeightFloor itself, so
            // no further clamping is needed on the raw weights.
            snapshot.familyBaseGrounded = source.familyBaseGrounded;
            snapshot.familyBaseSkewed = source.familyBaseSkewed;
            snapshot.zeroPassionInwardBonus = source.zeroPassionInwardBonus;
            snapshot.creepjoinerInwardBonus = source.creepjoinerInwardBonus;
            snapshot.groundedNoBurningBonus = source.groundedNoBurningBonus;
            snapshot.burningIntenseThreshold = source.burningIntenseThreshold;
            snapshot.burningIntenseBonus = source.burningIntenseBonus;
            snapshot.focusThreshold = source.focusThreshold;
            snapshot.focusMinTotalPoints = source.focusMinTotalPoints;
            snapshot.focusAnxiousBonus = source.focusAnxiousBonus;
            snapshot.memberBaseWeight = source.memberBaseWeight;
            snapshot.comboBonus = source.comboBonus;
            snapshot.continuityBonus = source.continuityBonus;
            snapshot.wildcardChance = Clamp01(source.wildcardChance);
            snapshot.wildcardGroundedBase = source.wildcardGroundedBase;
            snapshot.wildcardSkewedBase = source.wildcardSkewedBase;
            snapshot.jitterMin = Math.Min(source.jitterMin, source.jitterMax);
            snapshot.jitterMax = Math.Max(source.jitterMin, source.jitterMax);
            snapshot.duplicatePenalty = Clamp01(source.duplicatePenalty);
            return snapshot;
        }

        private static float Clamp01(float value)
        {
            return Math.Max(0f, Math.Min(1f, value));
        }
    }
}
