// Pure persona-bond state machine frozen in Royalty Phase 0 and consumed by Phase 2's guarded
// callbacks. It decides state and planned page ownership without touching a Pawn, Thing, or save.
using System;

namespace PawnDiary
{
    /// <summary>Deterministic persona formation, separation, recovery, and ending policy.</summary>
    internal static class PersonaLifecyclePolicy
    {
        /// <summary>
        /// Resolves overlapping cleanup evidence in the frozen R1 precedence order. Callers still
        /// need exact source evidence; this method only prevents a lower-priority cleanup callback
        /// from relabeling a death, destruction, or transfer.
        /// </summary>
        public static string ClassifyEndCause(
            bool pawnDeath,
            bool weaponDestroyed,
            bool exactTransfer,
            bool unknownUncode,
            bool mapRemoval)
        {
            if (pawnDeath) return PersonaEndCauseTokens.PawnDeath;
            if (weaponDestroyed) return PersonaEndCauseTokens.WeaponDestroyed;
            if (exactTransfer) return PersonaEndCauseTokens.Transfer;
            if (unknownUncode) return PersonaEndCauseTokens.UnknownUncode;
            if (mapRemoval) return PersonaEndCauseTokens.MapRemoval;
            return PersonaEndCauseTokens.None;
        }

        /// <summary>Evaluates one exact observation and returns a copied next state.</summary>
        public static PersonaLifecycleDecision Evaluate(
            PersonaBondStateSnapshot current,
            PersonaLifecycleObservation observation,
            RoyaltyPolicySnapshot policy)
        {
            PersonaLifecycleDecision decision = new PersonaLifecycleDecision
            {
                nextState = CopyState(current)
            };
            RoyaltyPolicySnapshot effective = policy ?? RoyaltyPolicySnapshot.CreateDefault();
            if (!effective.enabled || observation == null || observation.tick < 0)
            {
                return decision;
            }

            NormalizeState(decision.nextState);
            string token = observation.observationToken ?? string.Empty;
            if (token == PersonaObservationTokens.Baseline)
            {
                return Baseline(decision, observation, effective);
            }

            if (token == PersonaObservationTokens.Coding)
            {
                if (!observation.normalPlay) return Baseline(decision, observation, effective);
                return BeginBond(decision, observation, false, effective);
            }

            if (token == PersonaObservationTokens.Transfer)
            {
                return BeginBond(decision, observation, true, effective);
            }

            if (!PersonaBondPhaseTokens.IsLive(decision.nextState.phaseToken)
                || !MatchesCurrentBond(decision.nextState, observation.weapon))
            {
                return decision;
            }

            if (token == PersonaObservationTokens.PawnDeath)
            {
                return End(decision, observation, PersonaEndCauseTokens.PawnDeath, true);
            }

            if (token == PersonaObservationTokens.Destroyed)
            {
                return End(decision, observation, PersonaEndCauseTokens.WeaponDestroyed, false);
            }

            if (token == PersonaObservationTokens.UnknownUncode)
            {
                // UnCode is ambiguous by itself. Preserve the live truth for later reconciliation.
                return decision;
            }

            if (token == PersonaObservationTokens.MapRemoved
                || token == PersonaObservationTokens.Unavailable)
            {
                // Off-map time cannot prove a separation. Cancel only an un-emitted pending timer.
                if (decision.nextState.phaseToken == PersonaBondPhaseTokens.SeparationPending)
                {
                    decision.nextState.phaseToken = PersonaBondPhaseTokens.Active;
                    decision.nextState.pendingSeparationTick = -1;
                    decision.stateChanged = true;
                }
                return decision;
            }

            if (token == PersonaObservationTokens.Primary)
            {
                // A steady primary observation is still meaningful saved truth: recovery duration is
                // measured from the last tick at which the bond was actually observed in hand. Without
                // this state-change flag the impure adapter discards the copied timestamp whenever the
                // phase itself stays Active.
                if (decision.nextState.lastPrimaryObservedTick != observation.tick)
                {
                    decision.nextState.lastPrimaryObservedTick = observation.tick;
                    decision.stateChanged = true;
                }
                if (decision.nextState.phaseToken == PersonaBondPhaseTokens.SeparationPending)
                {
                    decision.nextState.phaseToken = PersonaBondPhaseTokens.Active;
                    decision.nextState.pendingSeparationTick = -1;
                    decision.stateChanged = true;
                }
                else if (decision.nextState.phaseToken == PersonaBondPhaseTokens.Separated)
                {
                    bool recordedSeparation = decision.nextState.separationEmitted;
                    decision.nextState.phaseToken = PersonaBondPhaseTokens.Active;
                    decision.nextState.pendingSeparationTick = -1;
                    decision.nextState.separationEmitted = false;
                    decision.stateChanged = true;
                    if (recordedSeparation)
                    {
                        decision.narrativePhase = PersonaNarrativePhaseTokens.BondRecovered;
                        decision.shouldEmit = observation.groupEnabled;
                    }
                }
                return decision;
            }

            if (token != PersonaObservationTokens.NotPrimary)
            {
                return decision;
            }

            if (decision.nextState.phaseToken == PersonaBondPhaseTokens.Active)
            {
                decision.nextState.phaseToken = PersonaBondPhaseTokens.SeparationPending;
                decision.nextState.pendingSeparationTick = observation.tick;
                decision.stateChanged = true;
                return decision;
            }

            if (decision.nextState.phaseToken != PersonaBondPhaseTokens.SeparationPending)
            {
                return decision;
            }

            int started = decision.nextState.pendingSeparationTick;
            int threshold = Math.Max(1, effective.separationThresholdTicks);
            if (started < 0)
            {
                decision.nextState.pendingSeparationTick = observation.tick;
                decision.stateChanged = true;
            }
            else if ((long)observation.tick - started >= threshold)
            {
                decision.nextState.phaseToken = PersonaBondPhaseTokens.Separated;
                decision.nextState.pendingSeparationTick = -1;
                // Pure policy can only plan output. The runtime adapter commits this as false first
                // and promotes it after a durable page exists; pure callers model that repository
                // result by supplying groupEnabled in their accepted/unaccepted matrix row.
                decision.nextState.separationEmitted = observation.groupEnabled;
                decision.narrativePhase = PersonaNarrativePhaseTokens.BondSeparated;
                decision.shouldEmit = observation.groupEnabled;
                decision.stateChanged = true;
            }
            return decision;
        }

        private static PersonaLifecycleDecision Baseline(
            PersonaLifecycleDecision decision,
            PersonaLifecycleObservation observation,
            RoyaltyPolicySnapshot policy)
        {
            PersonaWeaponSnapshot weapon = observation.weapon;
            if (!ValidBond(weapon)) return decision;
            decision.nextState = StateForNewBond(
                weapon, Math.Max(1, decision.nextState.bondEpoch), observation.tick, policy);
            // An old bond may already have killed many times. Conservatively consume the "first" claim.
            decision.nextState.firstConsequentialKillObserved = true;
            decision.stateChanged = true;
            return decision;
        }

        private static PersonaLifecycleDecision BeginBond(
            PersonaLifecycleDecision decision,
            PersonaLifecycleObservation observation,
            bool exactTransfer,
            RoyaltyPolicySnapshot policy)
        {
            PersonaWeaponSnapshot weapon = observation.weapon;
            if (!ValidBond(weapon)) return decision;

            bool sameWeapon = Same(decision.nextState.weaponThingId, weapon.weaponThingId);
            bool samePawn = Same(decision.nextState.currentPawnId, weapon.codedPawnId);
            if (PersonaBondPhaseTokens.IsLive(decision.nextState.phaseToken) && sameWeapon && samePawn)
            {
                return decision; // Repeated equip/coding is ordinary lifecycle noise.
            }

            if (exactTransfer && (!sameWeapon || samePawn || !PersonaBondPhaseTokens.IsLive(decision.nextState.phaseToken)))
            {
                return decision;
            }

            int epoch = sameWeapon ? Math.Max(1, decision.nextState.bondEpoch + 1) : 1;
            string previousPawn = sameWeapon ? decision.nextState.currentPawnId : string.Empty;
            decision.nextState = StateForNewBond(weapon, epoch, observation.tick, policy);
            decision.nextState.previousPawnId = previousPawn;
            decision.stateChanged = true;
            decision.narrativePhase = PersonaNarrativePhaseTokens.BondFormed;
            decision.shouldEmit = observation.groupEnabled;
            decision.includesExactPreviousBond = exactTransfer && previousPawn.Length > 0;
            return decision;
        }

        private static PersonaLifecycleDecision End(
            PersonaLifecycleDecision decision,
            PersonaLifecycleObservation observation,
            string cause,
            bool deathOwns)
        {
            decision.nextState.phaseToken = PersonaBondPhaseTokens.Ended;
            decision.nextState.pendingSeparationTick = -1;
            decision.nextState.endedTick = observation.tick;
            decision.nextState.endCauseToken = cause;
            decision.stateChanged = true;
            decision.deathOwnsEnding = deathOwns;
            if (!deathOwns)
            {
                decision.narrativePhase = PersonaNarrativePhaseTokens.BondEnded;
                decision.shouldEmit = observation.groupEnabled;
            }
            return decision;
        }

        private static PersonaBondStateSnapshot StateForNewBond(
            PersonaWeaponSnapshot weapon,
            int epoch,
            int tick,
            RoyaltyPolicySnapshot policy)
        {
            PersonaBondStateSnapshot state = new PersonaBondStateSnapshot
            {
                weaponThingId = Clean(weapon.weaponThingId),
                weaponDefName = Clean(weapon.weaponDefName),
                lastDisplayName = Clean(weapon.displayName),
                bondEpoch = epoch,
                currentPawnId = Clean(weapon.codedPawnId),
                currentPawnName = Clean(weapon.codedPawnName),
                phaseToken = PersonaBondPhaseTokens.Active,
                bondStartedTick = tick,
                lastPrimaryObservedTick = weapon.isCurrentlyPrimary ? tick : -1,
                traits = PersonaTraitPolicy.NormalizeFacts(weapon.traits, policy)
            };
            return state;
        }

        private static bool ValidBond(PersonaWeaponSnapshot weapon)
        {
            return weapon != null && !weapon.isDestroyed
                && SafeId(weapon.weaponThingId) && SafeId(weapon.codedPawnId);
        }

        private static bool MatchesCurrentBond(PersonaBondStateSnapshot state, PersonaWeaponSnapshot weapon)
        {
            return weapon != null && Same(state.weaponThingId, weapon.weaponThingId)
                && Same(state.currentPawnId, weapon.codedPawnId);
        }

        private static void NormalizeState(PersonaBondStateSnapshot state)
        {
            if (!PersonaBondPhaseTokens.IsKnown(state.phaseToken))
            {
                state.phaseToken = PersonaBondPhaseTokens.Untracked;
            }
            state.bondEpoch = Math.Max(0, state.bondEpoch);
            if (state.phaseToken != PersonaBondPhaseTokens.SeparationPending)
                state.pendingSeparationTick = -1;
        }

        private static PersonaBondStateSnapshot CopyState(PersonaBondStateSnapshot source)
        {
            if (source == null) return new PersonaBondStateSnapshot();
            return new PersonaBondStateSnapshot
            {
                weaponThingId = Clean(source.weaponThingId),
                weaponDefName = Clean(source.weaponDefName),
                lastDisplayName = Clean(source.lastDisplayName),
                bondEpoch = source.bondEpoch,
                currentPawnId = Clean(source.currentPawnId),
                currentPawnName = Clean(source.currentPawnName),
                previousPawnId = Clean(source.previousPawnId),
                phaseToken = source.phaseToken ?? PersonaBondPhaseTokens.Untracked,
                bondStartedTick = source.bondStartedTick,
                pendingSeparationTick = source.pendingSeparationTick,
                separationEmitted = source.separationEmitted,
                firstConsequentialKillObserved = source.firstConsequentialKillObserved,
                firstConsequentialKillEventRecorded = source.firstConsequentialKillEventRecorded,
                lastPrimaryObservedTick = source.lastPrimaryObservedTick,
                endedTick = source.endedTick,
                endCauseToken = source.endCauseToken ?? PersonaEndCauseTokens.None,
                traits = PersonaTraitPolicy.CopyFacts(source.traits)
            };
        }

        private static bool SafeId(string value)
        {
            string cleaned = Clean(value);
            return cleaned.Length > 0 && cleaned.IndexOf('|') < 0;
        }

        private static bool Same(string left, string right)
        {
            string a = Clean(left);
            string b = Clean(right);
            return a.Length > 0 && string.Equals(a, b, StringComparison.Ordinal);
        }

        private static string Clean(string value)
        {
            return (value ?? string.Empty).Trim();
        }
    }
}
