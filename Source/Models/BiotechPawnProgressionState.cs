// Per-pawn, additive Biotech progression bookkeeping. Growth consumption and the nested gene
// observation baseline live here so later mechanitor/bond work can extend one stable parent row
// instead of adding more top-level PawnDiaryRecord fields.
//
// New to C#/RimWorld? See AGENTS.md ("IExposable").
using System;
using System.Collections.Generic;
using PawnDiary.Capture;
using Verse;

namespace PawnDiary
{
    /// <summary>Saved per-pawn Biotech observation state that is not a narrative history database.</summary>
    public class BiotechPawnProgressionState : IExposable
    {
        public int growthObservationVersion = 1;
        public List<int> consumedGrowthAges = new List<int>();
        public GeneIdentityObservationState geneIdentityObservation = new GeneIdentityObservationState();
        internal MechanitorObservationState mechanitorObservation = new MechanitorObservationState();
        internal int bondObservationVersion;
        internal List<PsychicBondObservationRow> psychicBondObservations =
            new List<PsychicBondObservationRow>();
        internal DeathrestObservationState deathrestObservation = new DeathrestObservationState();

        public void ExposeData()
        {
            Scribe_Values.Look(
                ref growthObservationVersion,
                BiotechSaveKeys.GrowthObservationVersion,
                1);
            Scribe_Collections.Look(ref consumedGrowthAges, "consumedGrowthAges", LookMode.Value);
            Scribe_Deep.Look(
                ref geneIdentityObservation,
                BiotechSaveKeys.GeneIdentityObservationState);
            Scribe_Deep.Look(
                ref mechanitorObservation,
                BiotechSaveKeys.MechanitorObservationState);
            Scribe_Values.Look(
                ref bondObservationVersion,
                BiotechSaveKeys.BondObservationVersion,
                0);
            Scribe_Collections.Look(
                ref psychicBondObservations,
                BiotechSaveKeys.PsychicBondObservations,
                LookMode.Deep);
            Scribe_Deep.Look(
                ref deathrestObservation,
                BiotechSaveKeys.DeathrestObservationState);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                Normalize();
            }
        }

        /// <summary>Repairs malformed ages and de-duplicates the three canonical growth birthdays.</summary>
        public void Normalize()
        {
            growthObservationVersion = Math.Max(1, growthObservationVersion);
            if (consumedGrowthAges == null)
            {
                consumedGrowthAges = new List<int>();
            }

            HashSet<int> seen = new HashSet<int>();
            for (int i = 0; i < consumedGrowthAges.Count; i++)
            {
                int age = consumedGrowthAges[i];
                if ((age != 7 && age != 10 && age != 13) || !seen.Add(age))
                {
                    consumedGrowthAges.RemoveAt(i);
                    i--;
                }
            }

            consumedGrowthAges.Sort();
            if (geneIdentityObservation == null)
            {
                geneIdentityObservation = new GeneIdentityObservationState();
            }
            geneIdentityObservation.Normalize();
            if (mechanitorObservation == null)
            {
                mechanitorObservation = new MechanitorObservationState();
            }
            mechanitorObservation.Normalize(
                MechanitorObservationState.HardMaximumMechs,
                MechanitorObservationState.HardMaximumBossCalls);
            bondObservationVersion = Math.Max(0, bondObservationVersion);
            if (psychicBondObservations == null)
            {
                psychicBondObservations = new List<PsychicBondObservationRow>();
            }
            PsychicBondLifecyclePolicy.NormalizeRows(
                psychicBondObservations,
                string.Empty,
                int.MaxValue,
                PsychicBondLifecyclePolicy.HardMaximumObservationRows);
            if (deathrestObservation == null)
            {
                deathrestObservation = new DeathrestObservationState();
            }
            DeathrestInterruptionPolicy.Normalize(deathrestObservation, int.MaxValue);
        }

        /// <summary>Returns whether canonical ownership for this age was already consumed.</summary>
        public bool HasConsumedGrowthAge(int age)
        {
            return consumedGrowthAges != null && consumedGrowthAges.Contains(age);
        }

        /// <summary>Marks a canonical age consumed even when player settings suppress the page.</summary>
        public void ConsumeGrowthAge(int age)
        {
            if (age != 7 && age != 10 && age != 13)
            {
                return;
            }

            if (consumedGrowthAges == null)
            {
                consumedGrowthAges = new List<int>();
            }

            if (!consumedGrowthAges.Contains(age))
            {
                consumedGrowthAges.Add(age);
                consumedGrowthAges.Sort();
            }
        }

        /// <summary>Returns the normalized nested gene observation row.</summary>
        public GeneIdentityObservationState EnsureGeneIdentityObservation()
        {
            if (geneIdentityObservation == null)
            {
                geneIdentityObservation = new GeneIdentityObservationState();
            }
            geneIdentityObservation.Normalize();
            return geneIdentityObservation;
        }

        /// <summary>Returns the normalized nested mechanitor observation row.</summary>
        internal MechanitorObservationState EnsureMechanitorObservation()
        {
            if (mechanitorObservation == null)
            {
                mechanitorObservation = new MechanitorObservationState();
            }
            mechanitorObservation.Normalize(
                MechanitorObservationState.HardMaximumMechs,
                MechanitorObservationState.HardMaximumBossCalls);
            return mechanitorObservation;
        }

        /// <summary>Returns the normalized bounded psychic-bond partner history.</summary>
        internal List<PsychicBondObservationRow> EnsurePsychicBondObservations()
        {
            if (psychicBondObservations == null)
            {
                psychicBondObservations = new List<PsychicBondObservationRow>();
            }
            return psychicBondObservations;
        }

        /// <summary>Returns the normalized interrupted-deathrest lifetime/cooldown row.</summary>
        internal DeathrestObservationState EnsureDeathrestObservation()
        {
            if (deathrestObservation == null)
            {
                deathrestObservation = new DeathrestObservationState();
            }
            DeathrestInterruptionPolicy.Normalize(deathrestObservation, int.MaxValue);
            return deathrestObservation;
        }
    }
}
