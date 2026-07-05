// Observed conditions, part 5 of the pure layer: the diff. This is the brain of the system and the
// reason the whole thing is testable — given the current tick, the saved active states, the live
// observations from this scan, and the per-Def timing snapshots, it returns exactly what should start,
// refresh, end, or be dropped. It reads NO live game state and NO clock of its own: "now" and every
// observation are passed in, so the same inputs always produce the same plan. See AGENTS.md.
using System.Collections.Generic;

namespace PawnDiary
{
    /// <summary>
    /// Stateless lifecycle diff for observed conditions. Ticks only gate debounce here; whether a
    /// condition is active is decided purely by "is it in this scan's observations?".
    /// </summary>
    internal static class ObservedConditionPolicy
    {
        /// <summary>
        /// Diffs <paramref name="observations"/> (what live state shows now) against
        /// <paramref name="savedStates"/> (what we remembered) using each condition's debounce timing
        /// from <paramref name="defs"/>, and returns the resulting decisions.
        /// </summary>
        public static ObservedConditionPlan Plan(int now, IList<ObservedConditionStateSnapshot> savedStates,
            IList<ObservedConditionObservation> observations, IList<ObservedConditionDefSnapshot> defs)
        {
            ObservedConditionPlan plan = new ObservedConditionPlan();

            // Only enabled, present Defs are passed in. A saved state whose key is absent here has lost
            // its Def (disabled or removed) and is dropped without an end page.
            Dictionary<string, ObservedConditionDefSnapshot> defByKey = IndexDefs(defs);

            // Saved states indexed by identity so an observation can find the row it refreshes.
            Dictionary<string, ObservedConditionStateSnapshot> stateByIdentity = IndexStates(savedStates);

            // Which identities live state still shows; used to find saved rows that have gone missing.
            HashSet<string> observedIdentities = new HashSet<string>();

            // 1) Walk the live observations: each one either starts a new condition or refreshes one.
            if (observations != null)
            {
                for (int i = 0; i < observations.Count; i++)
                {
                    ObservedConditionObservation observation = observations[i];
                    if (observation == null || string.IsNullOrEmpty(observation.conditionKey))
                    {
                        continue;
                    }

                    ObservedConditionDefSnapshot def;
                    if (!defByKey.TryGetValue(observation.conditionKey, out def))
                    {
                        // Observation for a Def that is not enabled/present: ignore it defensively.
                        continue;
                    }

                    string identity = ObservedConditionStateSnapshot.Identity(
                        observation.conditionKey, observation.scope, observation.mapUniqueId,
                        observation.subjectPawnId);

                    // First seen this scan wins; a duplicate observation of the same identity is ignored.
                    if (!observedIdentities.Add(identity))
                    {
                        continue;
                    }

                    ObservedConditionStateSnapshot existing;
                    if (stateByIdentity.TryGetValue(identity, out existing))
                    {
                        plan.decisions.Add(RefreshExisting(existing, observation, def, now));
                    }
                    else
                    {
                        plan.decisions.Add(StartNew(observation, def, now));
                    }
                }
            }

            // 2) Walk the saved states: any identity NOT observed this scan is missing and may end.
            if (savedStates != null)
            {
                for (int i = 0; i < savedStates.Count; i++)
                {
                    ObservedConditionStateSnapshot saved = savedStates[i];
                    if (saved == null || string.IsNullOrEmpty(saved.conditionKey))
                    {
                        continue;
                    }

                    string identity = saved.IdentityKey();
                    if (observedIdentities.Contains(identity))
                    {
                        continue; // still observed — already handled in step 1.
                    }

                    ObservedConditionDefSnapshot def;
                    if (!defByKey.TryGetValue(saved.conditionKey, out def))
                    {
                        // Def disabled or removed: forget the condition quietly, no end page.
                        plan.decisions.Add(Drop(saved));
                        continue;
                    }

                    plan.decisions.Add(EndMissing(saved, def, now));
                }
            }

            return plan;
        }

        // A brand-new observation: create its state. If the start debounce is already satisfied (which
        // includes debounce 0), record the start immediately; otherwise hold it pending.
        private static ObservedConditionDecision StartNew(ObservedConditionObservation observation,
            ObservedConditionDefSnapshot def, int now)
        {
            ObservedConditionStateSnapshot state = new ObservedConditionStateSnapshot
            {
                conditionDefName = observation.conditionDefName,
                conditionKey = observation.conditionKey,
                scope = observation.scope,
                mapUniqueId = observation.mapUniqueId,
                subjectPawnId = observation.subjectPawnId,
                firstObservedTick = now,
                lastObservedTick = now,
                firstMissingTick = -1,
                startRecorded = false,
                endRecorded = false
            };
            ApplyEvidence(state, observation);

            if (now - state.firstObservedTick >= def.startDebounceTicks)
            {
                state.startRecorded = true;
                return Decision(ObservedConditionDecisionKind.StartRecorded, state, false, true);
            }

            return Decision(ObservedConditionDecisionKind.StartPending, state, false, false);
        }

        // A still-observed condition: refresh its tick and evidence, and if it has now passed the start
        // debounce for the first time, record the start. Never records a duplicate start.
        private static ObservedConditionDecision RefreshExisting(ObservedConditionStateSnapshot existing,
            ObservedConditionObservation observation, ObservedConditionDefSnapshot def, int now)
        {
            ObservedConditionStateSnapshot state = existing.Clone();
            state.conditionDefName = observation.conditionDefName ?? state.conditionDefName;
            state.lastObservedTick = now;
            state.firstMissingTick = -1; // observed again — cancel any pending end.
            ApplyEvidence(state, observation);

            if (!state.startRecorded && now - state.firstObservedTick >= def.startDebounceTicks)
            {
                state.startRecorded = true;
                return Decision(ObservedConditionDecisionKind.StartRecorded, state, false, true);
            }

            return Decision(ObservedConditionDecisionKind.Refresh, state, false, false);
        }

        // A saved condition that live state no longer shows: anchor the missing-since tick, then end it
        // once the end debounce elapses. A condition that never recorded its start just drops silently.
        private static ObservedConditionDecision EndMissing(ObservedConditionStateSnapshot saved,
            ObservedConditionDefSnapshot def, int now)
        {
            ObservedConditionStateSnapshot state = saved.Clone();
            if (state.firstMissingTick < 0)
            {
                state.firstMissingTick = now;
            }

            if (now - state.firstMissingTick >= def.endDebounceTicks)
            {
                if (state.startRecorded)
                {
                    state.endRecorded = true;
                    return Decision(ObservedConditionDecisionKind.EndRecorded, state, true, true);
                }

                return Decision(ObservedConditionDecisionKind.DropStale, state, true, false);
            }

            return Decision(ObservedConditionDecisionKind.EndPending, state, false, false);
        }

        private static ObservedConditionDecision Drop(ObservedConditionStateSnapshot saved)
        {
            return Decision(ObservedConditionDecisionKind.DropStale, saved.Clone(), true, false);
        }

        private static void ApplyEvidence(ObservedConditionStateSnapshot state,
            ObservedConditionObservation observation)
        {
            state.lastSeenEvidenceDefName = observation.evidenceDefName;
            state.lastSeenEvidenceLabel = observation.evidenceLabel;
            state.lastSeenEvidenceCount = observation.evidenceCount;
        }

        private static ObservedConditionDecision Decision(ObservedConditionDecisionKind kind,
            ObservedConditionStateSnapshot state, bool removeState, bool recordPage)
        {
            return new ObservedConditionDecision
            {
                kind = kind,
                state = state,
                removeState = removeState,
                recordPage = recordPage
            };
        }

        private static Dictionary<string, ObservedConditionDefSnapshot> IndexDefs(
            IList<ObservedConditionDefSnapshot> defs)
        {
            Dictionary<string, ObservedConditionDefSnapshot> byKey =
                new Dictionary<string, ObservedConditionDefSnapshot>();
            if (defs == null)
            {
                return byKey;
            }

            for (int i = 0; i < defs.Count; i++)
            {
                ObservedConditionDefSnapshot def = defs[i];
                if (def != null && !string.IsNullOrEmpty(def.conditionKey) && !byKey.ContainsKey(def.conditionKey))
                {
                    byKey[def.conditionKey] = def;
                }
            }

            return byKey;
        }

        private static Dictionary<string, ObservedConditionStateSnapshot> IndexStates(
            IList<ObservedConditionStateSnapshot> savedStates)
        {
            Dictionary<string, ObservedConditionStateSnapshot> byIdentity =
                new Dictionary<string, ObservedConditionStateSnapshot>();
            if (savedStates == null)
            {
                return byIdentity;
            }

            for (int i = 0; i < savedStates.Count; i++)
            {
                ObservedConditionStateSnapshot state = savedStates[i];
                if (state == null || string.IsNullOrEmpty(state.conditionKey))
                {
                    continue;
                }

                string identity = state.IdentityKey();
                if (!byIdentity.ContainsKey(identity))
                {
                    byIdentity[identity] = state;
                }
            }

            return byIdentity;
        }
    }
}
