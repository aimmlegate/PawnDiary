// Saved Royalty observation records. These rows contain only primitive copied facts; live pawns,
// weapons, titles, factions, and Defs remain behind DlcContext and are never Scribed here.
//
// New to C#/RimWorld? See AGENTS.md ("IExposable").
using System;
using System.Collections.Generic;
using Verse;

namespace PawnDiary
{
    /// <summary>Frozen save keys and explicit schema versions for the Royalty observation ledger.</summary>
    internal static class RoyaltySaveKeys
    {
        public const string PersonaObservationVersion = "royaltyPersonaObservationVersion";
        public const string PersonaBonds = "royaltyPersonaBonds";
        public const string PawnObservationState = "royaltyObservationState";
        public const string PawnObservationVersion = "royaltyObservationVersion";
        public const string ObservationAvailable = "royaltyObservationAvailable";
        public const string TitleObservations = "royaltyTitleObservations";
    }

    /// <summary>Deep-scribed persona-trait facts retained for a later exact bond ending.</summary>
    public sealed class PersonaTraitState : IExposable
    {
        public string traitDefName = string.Empty;
        public string label = string.Empty;
        public string description = string.Empty;
        public string workerTypeToken = string.Empty;
        public bool hasKillThought;
        public bool hasBondedThought;
        public bool hasBondedHediff;
        public bool hasEquippedHediff;

        public void ExposeData()
        {
            Scribe_Values.Look(ref traitDefName, "traitDefName");
            Scribe_Values.Look(ref label, "label");
            Scribe_Values.Look(ref description, "description");
            Scribe_Values.Look(ref workerTypeToken, "workerTypeToken");
            Scribe_Values.Look(ref hasKillThought, "hasKillThought", false);
            Scribe_Values.Look(ref hasBondedThought, "hasBondedThought", false);
            Scribe_Values.Look(ref hasBondedHediff, "hasBondedHediff", false);
            Scribe_Values.Look(ref hasEquippedHediff, "hasEquippedHediff", false);
        }

        internal PersonaTraitFact ToSnapshot()
        {
            return new PersonaTraitFact
            {
                traitDefName = traitDefName,
                label = label,
                description = description,
                workerTypeToken = workerTypeToken,
                hasKillThought = hasKillThought,
                hasBondedThought = hasBondedThought,
                hasBondedHediff = hasBondedHediff,
                hasEquippedHediff = hasEquippedHediff
            };
        }

        internal static PersonaTraitState FromSnapshot(PersonaTraitFact source)
        {
            if (source == null) return null;
            return new PersonaTraitState
            {
                traitDefName = source.traitDefName,
                label = source.label,
                description = source.description,
                workerTypeToken = source.workerTypeToken,
                hasKillThought = source.hasKillThought,
                hasBondedThought = source.hasBondedThought,
                hasBondedHediff = source.hasBondedHediff,
                hasEquippedHediff = source.hasEquippedHediff
            };
        }
    }

    /// <summary>Deep-scribed relationship state keyed by a persona weapon's stable Thing ID.</summary>
    public sealed class PersonaBondState : IExposable
    {
        public string weaponThingId = string.Empty;
        public string weaponDefName = string.Empty;
        public string lastDisplayName = string.Empty;
        public int bondEpoch;
        public string currentPawnId = string.Empty;
        public string currentPawnName = string.Empty;
        public string previousPawnId = string.Empty;
        public string phaseToken = PersonaBondPhaseTokens.Untracked;
        public int bondStartedTick = -1;
        public int pendingSeparationTick = -1;
        public bool separationEmitted;
        public bool firstConsequentialKillObserved;
        public bool firstConsequentialKillEventRecorded;
        public int lastPrimaryObservedTick = -1;
        public int endedTick = -1;
        public string endCauseToken = PersonaEndCauseTokens.None;
        public List<PersonaTraitState> traits = new List<PersonaTraitState>();

        public void ExposeData()
        {
            Scribe_Values.Look(ref weaponThingId, "weaponThingId");
            Scribe_Values.Look(ref weaponDefName, "weaponDefName");
            Scribe_Values.Look(ref lastDisplayName, "lastDisplayName");
            Scribe_Values.Look(ref bondEpoch, "bondEpoch", 0);
            Scribe_Values.Look(ref currentPawnId, "currentPawnId");
            Scribe_Values.Look(ref currentPawnName, "currentPawnName");
            Scribe_Values.Look(ref previousPawnId, "previousPawnId");
            Scribe_Values.Look(ref phaseToken, "phaseToken", PersonaBondPhaseTokens.Untracked);
            Scribe_Values.Look(ref bondStartedTick, "bondStartedTick", -1);
            Scribe_Values.Look(ref pendingSeparationTick, "pendingSeparationTick", -1);
            Scribe_Values.Look(ref separationEmitted, "separationEmitted", false);
            Scribe_Values.Look(ref firstConsequentialKillObserved, "firstConsequentialKillObserved", false);
            Scribe_Values.Look(ref firstConsequentialKillEventRecorded, "firstConsequentialKillEventRecorded", false);
            Scribe_Values.Look(ref lastPrimaryObservedTick, "lastPrimaryObservedTick", -1);
            Scribe_Values.Look(ref endedTick, "endedTick", -1);
            Scribe_Values.Look(ref endCauseToken, "endCauseToken", PersonaEndCauseTokens.None);
            Scribe_Collections.Look(ref traits, "traits", LookMode.Deep);

            if (Scribe.mode == LoadSaveMode.PostLoadInit) Normalize();
        }

        /// <summary>Repairs one loaded row using the XML trait cap and defensive hard limits.</summary>
        public void Normalize()
        {
            PersonaBondState normalized = FromSnapshot(RoyaltyStatePersistence.NormalizePersona(
                ToSnapshot(),
                DiaryRoyaltyPolicy.Snapshot().maximumTraitCandidates));
            Apply(normalized);
        }

        internal PersonaBondStateSnapshot ToSnapshot()
        {
            List<PersonaTraitFact> copied = new List<PersonaTraitFact>();
            if (traits != null)
                for (int i = 0; i < traits.Count; i++)
                    if (traits[i] != null) copied.Add(traits[i].ToSnapshot());
            return new PersonaBondStateSnapshot
            {
                weaponThingId = weaponThingId,
                weaponDefName = weaponDefName,
                lastDisplayName = lastDisplayName,
                bondEpoch = bondEpoch,
                currentPawnId = currentPawnId,
                currentPawnName = currentPawnName,
                previousPawnId = previousPawnId,
                phaseToken = phaseToken,
                bondStartedTick = bondStartedTick,
                pendingSeparationTick = pendingSeparationTick,
                separationEmitted = separationEmitted,
                firstConsequentialKillObserved = firstConsequentialKillObserved,
                firstConsequentialKillEventRecorded = firstConsequentialKillEventRecorded,
                lastPrimaryObservedTick = lastPrimaryObservedTick,
                endedTick = endedTick,
                endCauseToken = endCauseToken,
                traits = copied
            };
        }

        internal static PersonaBondState FromSnapshot(PersonaBondStateSnapshot source)
        {
            if (source == null) return null;
            PersonaBondState result = new PersonaBondState
            {
                weaponThingId = source.weaponThingId,
                weaponDefName = source.weaponDefName,
                lastDisplayName = source.lastDisplayName,
                bondEpoch = source.bondEpoch,
                currentPawnId = source.currentPawnId,
                currentPawnName = source.currentPawnName,
                previousPawnId = source.previousPawnId,
                phaseToken = source.phaseToken,
                bondStartedTick = source.bondStartedTick,
                pendingSeparationTick = source.pendingSeparationTick,
                separationEmitted = source.separationEmitted,
                firstConsequentialKillObserved = source.firstConsequentialKillObserved,
                firstConsequentialKillEventRecorded = source.firstConsequentialKillEventRecorded,
                lastPrimaryObservedTick = source.lastPrimaryObservedTick,
                endedTick = source.endedTick,
                endCauseToken = source.endCauseToken,
                traits = new List<PersonaTraitState>()
            };
            if (source.traits != null)
                for (int i = 0; i < source.traits.Count; i++)
                {
                    PersonaTraitState trait = PersonaTraitState.FromSnapshot(source.traits[i]);
                    if (trait != null) result.traits.Add(trait);
                }
            return result;
        }

        private void Apply(PersonaBondState source)
        {
            if (source == null) return;
            weaponThingId = source.weaponThingId;
            weaponDefName = source.weaponDefName;
            lastDisplayName = source.lastDisplayName;
            bondEpoch = source.bondEpoch;
            currentPawnId = source.currentPawnId;
            currentPawnName = source.currentPawnName;
            previousPawnId = source.previousPawnId;
            phaseToken = source.phaseToken;
            bondStartedTick = source.bondStartedTick;
            pendingSeparationTick = source.pendingSeparationTick;
            separationEmitted = source.separationEmitted;
            firstConsequentialKillObserved = source.firstConsequentialKillObserved;
            firstConsequentialKillEventRecorded = source.firstConsequentialKillEventRecorded;
            lastPrimaryObservedTick = source.lastPrimaryObservedTick;
            endedTick = source.endedTick;
            endCauseToken = source.endCauseToken;
            traits = source.traits;
        }
    }

    /// <summary>Deep-scribed highest-title baseline for one faction.</summary>
    public sealed class RoyalTitleObservationState : IExposable
    {
        public string factionId = string.Empty;
        public string factionName = string.Empty;
        public string titleDefName = string.Empty;
        public string titleLabel = string.Empty;
        public int seniority;
        public int lastObservedTick = -1;

        public void ExposeData()
        {
            Scribe_Values.Look(ref factionId, "factionId");
            Scribe_Values.Look(ref factionName, "factionName");
            Scribe_Values.Look(ref titleDefName, "titleDefName");
            Scribe_Values.Look(ref titleLabel, "titleLabel");
            Scribe_Values.Look(ref seniority, "seniority", 0);
            Scribe_Values.Look(ref lastObservedTick, "lastObservedTick", -1);
        }

        internal RoyalTitleObservationSnapshot ToSnapshot()
        {
            return new RoyalTitleObservationSnapshot
            {
                factionId = factionId,
                factionName = factionName,
                titleDefName = titleDefName,
                titleLabel = titleLabel,
                seniority = seniority,
                lastObservedTick = lastObservedTick
            };
        }

        internal static RoyalTitleObservationState FromSnapshot(RoyalTitleObservationSnapshot source)
        {
            return source == null ? null : new RoyalTitleObservationState
            {
                factionId = source.factionId,
                factionName = source.factionName,
                titleDefName = source.titleDefName,
                titleLabel = source.titleLabel,
                seniority = source.seniority,
                lastObservedTick = source.lastObservedTick
            };
        }
    }

    /// <summary>Versioned per-pawn Royalty baselines nested in existing progression state.</summary>
    public sealed class RoyaltyPawnProgressionState : IExposable
    {
        public int observationVersion;
        // False means the last observer pass could not read Royalty state. On later reactivation the
        // component silently rebaselines current truth before allowing new edges.
        public bool observationAvailable;
        public List<RoyalTitleObservationState> titleObservations = new List<RoyalTitleObservationState>();

        public void ExposeData()
        {
            Scribe_Values.Look(ref observationVersion, RoyaltySaveKeys.PawnObservationVersion, 0);
            Scribe_Values.Look(ref observationAvailable, RoyaltySaveKeys.ObservationAvailable, false);
            Scribe_Collections.Look(ref titleObservations, RoyaltySaveKeys.TitleObservations, LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit) Normalize();
        }

        /// <summary>Normalizes rows without turning a missing version-zero field into a current baseline.</summary>
        public void Normalize()
        {
            observationVersion = Math.Max(0, Math.Min(
                RoyaltyStatePersistence.CurrentObservationVersion,
                observationVersion));
            List<RoyalTitleObservationSnapshot> source = new List<RoyalTitleObservationSnapshot>();
            if (titleObservations != null)
                for (int i = 0; i < titleObservations.Count; i++)
                    if (titleObservations[i] != null) source.Add(titleObservations[i].ToSnapshot());
            List<RoyalTitleObservationSnapshot> normalized = RoyaltyStatePersistence.NormalizeTitleObservations(
                source,
                RoyaltyStatePersistence.HardMaximumTitleObservations);
            titleObservations = new List<RoyalTitleObservationState>();
            for (int i = 0; i < normalized.Count; i++)
                titleObservations.Add(RoyalTitleObservationState.FromSnapshot(normalized[i]));
        }

        internal void Baseline(IList<RoyalTitleSnapshot> currentTitles, int currentPsylinkLevel, int tick,
            PawnProgressionState parent)
        {
            List<RoyalTitleObservationSnapshot> rows = RoyaltyStatePersistence.BaselineTitles(currentTitles, tick);
            titleObservations = new List<RoyalTitleObservationState>();
            for (int i = 0; i < rows.Count; i++)
                titleObservations.Add(RoyalTitleObservationState.FromSnapshot(rows[i]));
            if (parent != null)
                parent.highestPsylinkLevelRecorded = Math.Max(
                    parent.highestPsylinkLevelRecorded,
                    Math.Max(0, currentPsylinkLevel));
            observationVersion = RoyaltyStatePersistence.CurrentObservationVersion;
            observationAvailable = true;
        }
    }
}
