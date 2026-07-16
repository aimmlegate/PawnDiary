// Verse-facing Scribe declarations for the detached canonical-birth snapshot and pending naming
// ownership. Pure contracts/policies stay under Source/Capture; this file adds only stable save keys.
//
// New to C#/RimWorld? See AGENTS.md ("IExposable", "DLC-safety", and "architecture barriers").
using System.Collections.Generic;
using Verse;

namespace PawnDiary.Capture
{
    /// <summary>Persists one exact adult participant without retaining a live Pawn.</summary>
    internal partial class FamilyParticipantFact : IExposable
    {
        public void ExposeData()
        {
            Scribe_Values.Look(ref pawnId, "pawnId");
            Scribe_Values.Look(ref displayName, "displayName");
            Scribe_Values.Look(ref roleToken, "roleToken");
            Scribe_Values.Look(ref eligible, "eligible", false);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                pawnId = pawnId ?? string.Empty;
                displayName = displayName ?? string.Empty;
                roleToken = roleToken ?? string.Empty;
            }
        }
    }

    /// <summary>Persists the exact birth facts captured before the naming decision was final.</summary>
    internal partial class BirthMutationSnapshot : IExposable
    {
        public void ExposeData()
        {
            Scribe_Values.Look(ref familyArcId, "familyArcId");
            Scribe_Values.Look(ref childId, "childId");
            Scribe_Values.Look(ref currentChildName, "currentChildName");
            Scribe_Deep.Look(ref birther, "birther");
            Scribe_Deep.Look(ref geneticMother, "geneticMother");
            Scribe_Deep.Look(ref father, "father");
            Scribe_Deep.Look(ref doctor, "doctor");
            Scribe_Values.Look(ref outcomeToken, "outcomeToken");
            Scribe_Values.Look(ref methodToken, "methodToken");
            Scribe_Values.Look(ref qualityBand, "qualityBand");
            Scribe_Values.Look(ref birtherDied, "birtherDied", false);
            Scribe_Values.Look(ref ritualBirth, "ritualBirth", false);
            Scribe_Values.Look(ref namingDeadline, "namingDeadline", 0);
            Scribe_Values.Look(ref namingResolved, "namingResolved", false);
            Scribe_Values.Look(ref birthTick, "birthTick", 0);
            Scribe_Values.Look(ref correlationId, "correlationId");
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                familyArcId = familyArcId ?? string.Empty;
                childId = childId ?? string.Empty;
                currentChildName = currentChildName ?? string.Empty;
                outcomeToken = outcomeToken ?? string.Empty;
                methodToken = methodToken ?? string.Empty;
                qualityBand = qualityBand ?? string.Empty;
                correlationId = correlationId ?? string.Empty;
            }
        }
    }

    /// <summary>Persists one writer selected at the canonical birth boundary.</summary>
    internal partial class BirthWriterFact : IExposable
    {
        public void ExposeData()
        {
            Scribe_Values.Look(ref pawnId, "pawnId");
            Scribe_Values.Look(ref displayName, "displayName");
            Scribe_Values.Look(ref roleToken, "roleToken");
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                pawnId = pawnId ?? string.Empty;
                displayName = displayName ?? string.Empty;
                roleToken = roleToken ?? string.Empty;
            }
        }
    }

    /// <summary>Persists the frozen one/two-writer ordering.</summary>
    internal partial class BirthWriterSelection : IExposable
    {
        public void ExposeData()
        {
            Scribe_Collections.Look(ref writers, "writers", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                writers = writers ?? new List<BirthWriterFact>();
            }
        }
    }

    /// <summary>Persists one writer's event-time prompt and display facts.</summary>
    internal partial class BirthWriterContextSnapshot : IExposable
    {
        public void ExposeData()
        {
            Scribe_Values.Look(ref pawnId, "pawnId");
            Scribe_Values.Look(ref displayName, "displayName");
            Scribe_Values.Look(ref pawnSummary, "pawnSummary");
            Scribe_Values.Look(ref surroundings, "surroundings");
            Scribe_Values.Look(ref continuity, "continuity");
            Scribe_Values.Look(ref pairContinuity, "pairContinuity");
            Scribe_Values.Look(ref lastOpener, "lastOpener");
            Scribe_Values.Look(ref previousEntryEnding, "previousEntryEnding");
            Scribe_Values.Look(ref weapon, "weapon");
            Scribe_Values.Look(ref staggeredIntensity, "staggeredIntensity", 0);
            Scribe_Values.Look(ref textDecorationFacts, "textDecorationFacts");
            Scribe_Values.Look(ref skipFirstPersonGeneration, "skipFirstPersonGeneration", false);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                pawnId = pawnId ?? string.Empty;
                displayName = displayName ?? string.Empty;
                pawnSummary = pawnSummary ?? string.Empty;
                surroundings = surroundings ?? string.Empty;
                continuity = continuity ?? string.Empty;
                pairContinuity = pairContinuity ?? string.Empty;
                lastOpener = lastOpener ?? string.Empty;
                previousEntryEnding = previousEntryEnding ?? string.Empty;
                weapon = weapon ?? string.Empty;
                textDecorationFacts = textDecorationFacts ?? string.Empty;
            }
        }
    }

    /// <summary>Persists the event-time chronology and writer contexts across a naming wait.</summary>
    internal partial class BirthEventContextSnapshot : IExposable
    {
        public void ExposeData()
        {
            Scribe_Values.Look(ref birthTick, "birthTick", 0);
            Scribe_Values.Look(ref birthDate, "birthDate");
            Scribe_Collections.Look(ref writers, "writers", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                birthDate = birthDate ?? string.Empty;
                writers = writers ?? new List<BirthWriterContextSnapshot>();
            }
        }
    }

    /// <summary>Saves one canonical birth until naming resolves or the bounded wait expires.</summary>
    internal partial class PendingBiotechBirthState : IExposable
    {
        public void ExposeData()
        {
            Scribe_Deep.Look(ref snapshot, "snapshot");
            Scribe_Deep.Look(ref writers, "writers");
            Scribe_Deep.Look(ref eventContext, "eventContext");
            Scribe_Values.Look(ref createdTick, "createdTick", 0);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                // Self-repair like every sibling type above, so a pre-normalization consumer can never
                // meet a null writer list or negative tick. A null snapshot stays null on purpose: the
                // component's PostLoadInit normalization (PendingBiotechBirthPolicy.Normalize) drops the
                // whole row because a birth without its frozen facts cannot be replayed truthfully.
                writers = writers ?? new BirthWriterSelection();
                createdTick = createdTick < 0 ? 0 : createdTick;
            }
        }
    }
}
