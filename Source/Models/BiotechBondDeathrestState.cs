// Verse Scribe adapters for the detached Biotech Phase-8 bond/deathrest rows. The pure contracts
// live under Source/Capture/Biotech so standalone tests never need RimWorld assemblies.
//
// New to C#/RimWorld? See AGENTS.md ("IExposable" and old-save normalization).
using PawnDiary.Capture;
using Verse;

namespace PawnDiary.Capture
{
    /// <summary>Serializes one bounded psychic-bond partner history row without live pawn references.</summary>
    internal partial class PsychicBondObservationRow : IExposable
    {
        public void ExposeData()
        {
            Scribe_Values.Look(ref partnerPawnId, "partnerPawnId", string.Empty);
            Scribe_Values.Look(ref bondEpoch, "bondEpoch", 0);
            Scribe_Values.Look(ref bonded, "bonded", false);
            Scribe_Values.Look(ref lastTransitionTick, "lastTransitionTick", 0);
        }
    }

    /// <summary>Serializes interrupted-deathrest lifetime and cooldown markers.</summary>
    internal partial class DeathrestObservationState : IExposable
    {
        public void ExposeData()
        {
            Scribe_Values.Look(
                ref observationVersion,
                BiotechSaveKeys.DeathrestObservationVersion,
                0);
            Scribe_Values.Look(
                ref severeInterruptionsRecorded,
                BiotechSaveKeys.DeathrestSevereInterruptionsRecorded,
                0);
            Scribe_Values.Look(
                ref lastRecordedTick,
                BiotechSaveKeys.DeathrestLastRecordedTick,
                -1);
        }
    }
}
