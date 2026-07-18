// Verse/Scribe persistence adapters for the pure mechanitor observation contracts. Only stable IDs,
// labels, ticks, and consumed flags are saved; live Pawn, Def, relation, and tracker references never
// cross the save boundary. New to C#/RimWorld? See AGENTS.md ("IExposable").
using PawnDiary.Capture;
using Verse;

namespace PawnDiary.Capture
{
    /// <summary>Scribe adapter for one observed mech tenure row.</summary>
    internal partial class MechanitorMechObservationState : IExposable
    {
        /// <summary>Reads or writes one primitive mech observation row.</summary>
        public void ExposeData()
        {
            Scribe_Values.Look(ref mechId, "mechId");
            Scribe_Values.Look(ref lastDisplayName, "lastDisplayName");
            Scribe_Values.Look(ref kindDefName, "kindDefName");
            Scribe_Values.Look(ref firstObservedTick, "firstObservedTick", 0);
            Scribe_Values.Look(ref lossObserved, "lossObserved", false);
        }
    }

    /// <summary>Scribe adapter for one exact boss call owned by its mechanitor caller.</summary>
    internal partial class MechanitorBossCallObservationState : IExposable
    {
        /// <summary>Reads or writes one primitive boss-call ownership row.</summary>
        public void ExposeData()
        {
            Scribe_Values.Look(ref bossgroupDefName, "bossgroupDefName");
            Scribe_Values.Look(ref bossDefName, "bossDefName");
            Scribe_Values.Look(ref bossKindDefName, "bossKindDefName");
            Scribe_Values.Look(ref bossLabel, "bossLabel");
            Scribe_Values.Look(ref bossPawnId, "bossPawnId");
            Scribe_Values.Look(ref calledTick, "calledTick", 0);
            Scribe_Values.Look(ref defeatedObserved, "defeatedObserved", false);
        }
    }

    /// <summary>Scribe adapter for the bounded per-controller Phase-6 observation row.</summary>
    internal partial class MechanitorObservationState : IExposable
    {
        /// <summary>Reads or writes the bounded per-controller observation graph.</summary>
        public void ExposeData()
        {
            Scribe_Values.Look(
                ref observationVersion,
                BiotechSaveKeys.MechanitorObservationVersion,
                0);
            Scribe_Values.Look(ref mechlinkPresent, "mechlinkPresent", false);
            Scribe_Values.Look(ref firstControlledPageConsumed, "firstControlledPageConsumed", false);
            Scribe_Values.Look(
                ref firstControlledCombatPageConsumed,
                "firstControlledCombatPageConsumed",
                false);
            Scribe_Collections.Look(ref observedMechs, "observedMechs", LookMode.Deep);
            Scribe_Collections.Look(ref bossCalls, "bossCalls", LookMode.Deep);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                // The pure hard ceilings are the final corruption guard. XML-specific smaller caps
                // are applied when the live policy snapshot is available on the main thread.
                Normalize(HardMaximumMechs, HardMaximumBossCalls);
            }
        }
    }
}
