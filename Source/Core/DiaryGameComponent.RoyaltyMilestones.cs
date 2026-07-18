// Royalty Phase-3 orchestration for the first consequential persona-weapon kill and the bonded
// wielder's death. Live Pawn/weapon reads stay at this impure component edge; qualification,
// trait selection, and context formatting remain detached and assembly-free.
using System;
using System.Collections.Generic;
using Verse;

namespace PawnDiary
{
    public partial class DiaryGameComponent
    {
        /// <summary>
        /// Consumes exact first-kill gameplay truth even when its group is disabled, then returns
        /// enrichment data only when the existing Tale is allowed to own a durable persona page.
        /// </summary>
        internal bool TryObserveRoyaltyPersonaMilestone(
            Pawn killer,
            Pawn victim,
            string taleDefName,
            string killerRoleToken,
            string victimRoleToken,
            int significance,
            bool exactKillScope,
            bool personaGroupEnabled,
            int tick,
            out PersonaWeaponSnapshot weapon,
            out PersonaBondStateSnapshot bond,
            out PersonaMilestoneDecision milestone)
        {
            weapon = null;
            bond = null;
            milestone = null;
            if (!RoyaltyPersonaRuntimeReady() || killer == null || victim == null
                || !IsDiaryEligible(killer)) return false;
            // SnapshotFreeColonists allocates a fresh list. The version gate is checked here as
            // well as inside the baseline routine because qualifying Tales are frequent after the
            // first observation and do not need to rebuild an already-established baseline.
            if (royaltyPersonaObservationVersion < RoyaltyStatePersistence.CurrentObservationVersion)
                BaselineRoyaltyStateIfNeeded(SnapshotFreeColonists());

            List<PersonaWeaponSnapshot> visible = DlcContext.CapturePersonaWeapons(killer);
            if (visible.Count != 1 || !visible[0].isCurrentlyPrimary) return false;
            PersonaWeaponSnapshot current = visible[0];
            int index = PersonaBondIndex(current.weaponThingId);
            PersonaBondState state = index >= 0 ? royaltyPersonaBonds[index] : null;
            if (state == null || state.bondEpoch < 1
                || !PersonaBondPhaseTokens.IsLive(state.phaseToken)
                || !string.Equals(state.currentPawnId, killer.GetUniqueLoadID(), StringComparison.Ordinal))
                return false;

            PersonaBondStateSnapshot previous = state.ToSnapshot();
            string eventIdentity = RoyaltyArcKeys.Persona(previous.weaponThingId, previous.bondEpoch)
                + "|" + (taleDefName ?? string.Empty)
                + "|" + (victim.GetUniqueLoadID() ?? string.Empty);
            PersonaMilestoneDecision decision = PersonaMilestonePolicy.Evaluate(
                new PersonaMilestoneObservation
                {
                    bond = previous,
                    currentWeapon = current,
                    taleDefName = taleDefName ?? string.Empty,
                    resolvedKillerRoleToken = killerRoleToken ?? string.Empty,
                    deathVictimRoleToken = victimRoleToken ?? string.Empty,
                    significance = Math.Max(0, significance),
                    victimPresent = true,
                    // Vanilla records KilledMajorThreat inside Pawn.DoKillSideEffects, before
                    // Pawn.Kill reaches health.SetDead(). The exact balanced Pawn.Kill scope is the
                    // death proof available at this callback; reading victim.Dead here always rejects
                    // a legitimate Tale because that later mutation has not happened yet.
                    victimDied = exactKillScope,
                    hasDeathContext = exactKillScope,
                    deathContextMatchesKiller = exactKillScope,
                    personaGroupEnabled = personaGroupEnabled,
                    // Durable acceptance is promoted separately below. This true means the caller is
                    // prepared to create the page if every earlier gate passed.
                    pageAccepted = true,
                    eventIdentity = eventIdentity
                },
                DiaryRoyaltyPolicy.Snapshot());
            if (!decision.qualifies) return false;

            if (decision.markObserved)
            {
                state.firstConsequentialKillObserved = true;
                royaltyPersonaObservationVersion = RoyaltyStatePersistence.CurrentObservationVersion;
            }

            weapon = current;
            bond = state.ToSnapshot();
            milestone = decision;
            return decision.enrichTale;
        }

        /// <summary>Marks page ownership only after TaleSignal created the durable canonical event.</summary>
        internal void MarkRoyaltyPersonaMilestoneAccepted(string weaponThingId, int bondEpoch)
        {
            int index = PersonaBondIndex(weaponThingId);
            PersonaBondState state = index >= 0 ? royaltyPersonaBonds[index] : null;
            if (state == null || state.bondEpoch != bondEpoch
                || !state.firstConsequentialKillObserved) return;
            state.firstConsequentialKillEventRecorded = true;
            royaltyPersonaObservationVersion = RoyaltyStatePersistence.CurrentObservationVersion;
        }

        /// <summary>
        /// Snapshots a live bond before Pawn.Kill lets vanilla UnCode it. The returned fragment is
        /// appended to the existing death context; it never creates a second ending page.
        /// </summary>
        internal string CaptureRoyaltyPersonaWielderDeathContext(Pawn pawn)
        {
            if (!RoyaltyPersonaRuntimeReady() || pawn == null) return string.Empty;
            BaselineRoyaltyStateIfNeeded(SnapshotFreeColonists());
            List<PersonaWeaponSnapshot> visible = DlcContext.CapturePersonaWeapons(pawn);
            // A living separation-pending/separated bond can still be coded to this pawn while the
            // weapon sits in another equipment slot. Death ends that exact bond too; primary status
            // is required for a kill milestone, not for preserving the wielder-death context.
            if (visible.Count != 1) return string.Empty;
            PersonaWeaponSnapshot weapon = visible[0];
            int index = PersonaBondIndex(weapon.weaponThingId);
            PersonaBondState state = index >= 0 ? royaltyPersonaBonds[index] : null;
            if (state == null || state.bondEpoch < 1
                || !PersonaBondPhaseTokens.IsLive(state.phaseToken)
                || !string.Equals(state.currentPawnId, pawn.GetUniqueLoadID(), StringComparison.Ordinal))
                return string.Empty;

            PersonaBondStateSnapshot bond = state.ToSnapshot();
            RoyaltyPolicySnapshot policy = DiaryRoyaltyPolicy.Snapshot();
            List<PersonaTraitFact> selected = PersonaTraitPolicy.Select(
                weapon.traits,
                PersonaTraitEventTokens.Ending,
                RoyaltyArcKeys.Persona(weapon.weaponThingId, bond.bondEpoch) + "|wielder_death",
                policy);
            return PersonaMilestoneContextFormatter.FormatWielderDeath(
                weapon, bond, selected, policy);
        }
    }
}
