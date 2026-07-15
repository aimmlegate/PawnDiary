// Verse-facing persistence for Biotech family continuity. The DTO fields and all normalization rules
// live in the pure Capture layer; these partial declarations add stable Scribe keys only.
//
// New to C#/RimWorld? See AGENTS.md ("IExposable", "DLC-safety", and "architecture barriers").
using System.Collections.Generic;
using Verse;

namespace PawnDiary.Capture
{
    /// <summary>Persists one adult's exact lesson/play/care evidence within a child family arc.</summary>
    internal partial class FamilySupportObservationState : IExposable
    {
        public void ExposeData()
        {
            Scribe_Values.Look(ref adultId, "adultId");
            Scribe_Values.Look(ref lastDisplayName, "lastDisplayName");
            Scribe_Values.Look(ref relationToken, "relationToken");
            Scribe_Values.Look(ref lessonCount, "lessonCount", 0);
            Scribe_Values.Look(ref babyPlayCount, "babyPlayCount", 0);
            Scribe_Values.Look(ref careCount, "careCount", 0);
            Scribe_Values.Look(ref summarizedLessonCount, "summarizedLessonCount", 0);
            Scribe_Values.Look(ref summarizedBabyPlayCount, "summarizedBabyPlayCount", 0);
            Scribe_Values.Look(ref summarizedCareCount, "summarizedCareCount", 0);
            Scribe_Values.Look(ref firstObservedTick, "firstObservedTick", 0);
            Scribe_Values.Look(ref lastObservedTick, "lastObservedTick", 0);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                adultId = adultId ?? string.Empty;
                lastDisplayName = lastDisplayName ?? string.Empty;
                relationToken = relationToken ?? string.Empty;
            }
        }
    }

    /// <summary>Saves one stable family arc without retaining live Pawn or Hediff references.</summary>
    internal partial class BiotechFamilyArcState : IExposable
    {
        public void ExposeData()
        {
            Scribe_Values.Look(ref familyArcId, "familyArcId");
            Scribe_Values.Look(ref pregnancyHediffId, "pregnancyHediffId");
            Scribe_Values.Look(ref laborHediffId, "laborHediffId");
            Scribe_Values.Look(ref childId, "childId");
            Scribe_Values.Look(ref birtherId, "birtherId");
            Scribe_Values.Look(ref geneticMotherId, "geneticMotherId");
            Scribe_Values.Look(ref fatherId, "fatherId");
            Scribe_Values.Look(ref birtherName, "birtherName");
            Scribe_Values.Look(ref geneticMotherName, "geneticMotherName");
            Scribe_Values.Look(ref fatherName, "fatherName");
            Scribe_Values.Look(ref openedTick, "openedTick", 0);
            Scribe_Values.Look(ref birthTick, "birthTick", 0);
            Scribe_Values.Look(ref lastObservedTick, "lastObservedTick", 0);
            Scribe_Values.Look(ref birthOutcomeToken, "birthOutcomeToken");
            Scribe_Values.Look(ref birthMethodToken, "birthMethodToken");
            Scribe_Values.Look(ref childNameAtBirth, "childNameAtBirth");
            Scribe_Values.Look(ref currentChildName, "currentChildName");
            Scribe_Values.Look(ref namingResolved, "namingResolved", false);
            Scribe_Values.Look(ref closed, "closed", false);
            Scribe_Values.Look(ref baselineOnly, "baselineOnly", false);
            Scribe_Values.Look(ref detailsCompacted, "detailsCompacted", false);
            Scribe_Collections.Look(ref supporters, "supporters", LookMode.Deep);
            Scribe_Collections.Look(ref recordedGrowthAges, "recordedGrowthAges", LookMode.Value);
            Scribe_Values.Look(ref lastSummarizedObservationTick, "lastSummarizedObservationTick", 0);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                familyArcId = familyArcId ?? string.Empty;
                pregnancyHediffId = pregnancyHediffId ?? string.Empty;
                laborHediffId = laborHediffId ?? string.Empty;
                childId = childId ?? string.Empty;
                birtherId = birtherId ?? string.Empty;
                geneticMotherId = geneticMotherId ?? string.Empty;
                fatherId = fatherId ?? string.Empty;
                birtherName = birtherName ?? string.Empty;
                geneticMotherName = geneticMotherName ?? string.Empty;
                fatherName = fatherName ?? string.Empty;
                birthOutcomeToken = birthOutcomeToken ?? string.Empty;
                birthMethodToken = birthMethodToken ?? string.Empty;
                childNameAtBirth = childNameAtBirth ?? string.Empty;
                currentChildName = currentChildName ?? string.Empty;
                supporters = supporters ?? new List<FamilySupportObservationState>();
                recordedGrowthAges = recordedGrowthAges ?? new List<int>();
            }
        }
    }
}
