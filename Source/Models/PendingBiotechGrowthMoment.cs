// Verse-facing Scribe declarations for the plain Biotech growth DTOs. The detached contracts live
// under Source/Capture so pure tests can compile them without RimWorld; these partial declarations
// add only stable save keys and null-safe PostLoadInit repair.
//
// New to C#/RimWorld? See AGENTS.md ("IExposable" and "DLC-safety").
using System;
using System.Collections.Generic;
using Verse;

namespace PawnDiary.Capture
{
    /// <summary>Persists one detached trait fact inside a pending growth snapshot.</summary>
    internal partial class GrowthTraitFact : IExposable
    {
        public void ExposeData()
        {
            Scribe_Values.Look(ref traitKey, "traitKey");
            Scribe_Values.Look(ref label, "label");
            Scribe_Values.Look(ref description, "description");

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                traitKey = traitKey ?? string.Empty;
                label = label ?? string.Empty;
                description = description ?? string.Empty;
            }
        }
    }

    /// <summary>Persists one detached skill/passion fact inside a pending growth snapshot.</summary>
    internal partial class GrowthSkillFact : IExposable
    {
        public void ExposeData()
        {
            Scribe_Values.Look(ref skillDefName, "skillDefName");
            Scribe_Values.Look(ref label, "label");
            Scribe_Values.Look(ref passion, "passion", BiotechPassionTokens.None);
            Scribe_Values.Look(ref level, "level", 0);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                skillDefName = skillDefName ?? string.Empty;
                label = label ?? string.Empty;
                passion = BiotechPassionTokens.Normalize(passion);
                level = Math.Max(0, level);
            }
        }
    }

    /// <summary>Persists the event-time pawn snapshot needed after a postponed growth letter.</summary>
    internal partial class GrowthPawnSnapshot : IExposable
    {
        public void ExposeData()
        {
            Scribe_Values.Look(ref pawnId, "pawnId");
            Scribe_Values.Look(ref displayName, "displayName");
            Scribe_Values.Look(ref biologicalAge, "biologicalAge", 0);
            Scribe_Values.Look(ref growthTier, "growthTier", 0);
            Scribe_Values.Look(ref shortName, "shortName");
            Scribe_Values.Look(ref hasNewResponsibilities, "hasNewResponsibilities", false);
            Scribe_Values.Look(ref capturedTick, "capturedTick", 0);
            Scribe_Collections.Look(ref traits, "traits", LookMode.Deep);
            Scribe_Collections.Look(ref skills, "skills", LookMode.Deep);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                pawnId = pawnId ?? string.Empty;
                displayName = displayName ?? string.Empty;
                shortName = shortName ?? string.Empty;
                traits = traits ?? new List<GrowthTraitFact>();
                skills = skills ?? new List<GrowthSkillFact>();
            }
        }
    }

    /// <summary>
    /// Saves ownership of one configured growth letter without saving the live letter object itself.
    /// </summary>
    internal partial class PendingBiotechGrowthMoment : IExposable
    {
        public void ExposeData()
        {
            Scribe_Values.Look(ref pawnId, "pawnId");
            Scribe_Values.Look(ref birthdayAge, "birthdayAge", 0);
            Scribe_Values.Look(ref birthdayTick, "birthdayTick", 0);
            Scribe_Values.Look(ref configuredTick, "configuredTick", 0);
            Scribe_Values.Look(ref growthTier, "growthTier", 0);
            Scribe_Values.Look(ref newResponsibilities, "newResponsibilities", false);
            Scribe_Values.Look(ref correlationId, "correlationId");
            Scribe_Values.Look(ref familyArcId, "familyArcId");
            Scribe_Deep.Look(ref birthdaySnapshot, "birthdaySnapshot");

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                pawnId = pawnId ?? string.Empty;
                correlationId = correlationId ?? string.Empty;
                familyArcId = familyArcId ?? string.Empty;
            }
        }
    }
}
