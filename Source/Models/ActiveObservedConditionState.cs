// Saved runtime state for one observed condition (Plan 12). Defs describe what an observed condition
// is and how it debounces; this model remembers which conditions are currently active in THIS save so
// a long-running threat (a raid, toxic fallout, gray-flesh evidence) survives save/load and is not
// re-announced or lost when the player reloads mid-state. Mirrors ActiveEventWindowState, but the
// lifecycle is driven by live scans (ObservedConditionPolicy), never by a fixed timeout.
//
// Save/load is additive: every field is written with Scribe defaults, null strings normalize to empty
// in PostLoadInit, and old saves without these rows simply load an empty list. New to C#/RimWorld?
// See AGENTS.md ("IExposable").
using Verse;

namespace PawnDiary
{
    /// <summary>
    /// One active observed condition, persisted with the game. The pure policy works on the Verse-free
    /// <see cref="ObservedConditionStateSnapshot"/>; this type only adds Scribe persistence and the
    /// conversions to/from that snapshot.
    /// </summary>
    public class ActiveObservedConditionState : IExposable
    {
        public string conditionDefName;
        public string conditionKey;
        // Stored as the enum's int so a future enum addition never breaks an old save's string parse.
        public ObservedConditionScope scope = ObservedConditionScope.Map;
        public int mapUniqueId = -1;
        public string subjectPawnId;
        public int firstObservedTick;
        public int lastObservedTick;
        public int firstMissingTick = -1;
        public string lastSeenEvidenceDefName;
        public string lastSeenEvidenceLabel;
        public int lastSeenEvidenceCount;
        public bool startRecorded;
        public bool endRecorded;

        public void ExposeData()
        {
            Scribe_Values.Look(ref conditionDefName, "conditionDefName");
            Scribe_Values.Look(ref conditionKey, "conditionKey");
            Scribe_Values.Look(ref scope, "scope", ObservedConditionScope.Map);
            Scribe_Values.Look(ref mapUniqueId, "mapUniqueId", -1);
            Scribe_Values.Look(ref subjectPawnId, "subjectPawnId");
            Scribe_Values.Look(ref firstObservedTick, "firstObservedTick");
            Scribe_Values.Look(ref lastObservedTick, "lastObservedTick");
            Scribe_Values.Look(ref firstMissingTick, "firstMissingTick", -1);
            Scribe_Values.Look(ref lastSeenEvidenceDefName, "lastSeenEvidenceDefName");
            Scribe_Values.Look(ref lastSeenEvidenceLabel, "lastSeenEvidenceLabel");
            Scribe_Values.Look(ref lastSeenEvidenceCount, "lastSeenEvidenceCount");
            Scribe_Values.Look(ref startRecorded, "startRecorded");
            Scribe_Values.Look(ref endRecorded, "endRecorded");

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                NormalizeOnLoad();
            }
        }

        /// <summary>Null-coalesces strings so the rest of the session never has to null-check them.</summary>
        public void NormalizeOnLoad()
        {
            conditionDefName = conditionDefName ?? string.Empty;
            conditionKey = string.IsNullOrWhiteSpace(conditionKey) ? conditionDefName : conditionKey;
            subjectPawnId = subjectPawnId ?? string.Empty;
            lastSeenEvidenceDefName = lastSeenEvidenceDefName ?? string.Empty;
            lastSeenEvidenceLabel = lastSeenEvidenceLabel ?? string.Empty;
        }

        /// <summary>Projects this saved row into the Verse-free snapshot the pure policy reads.</summary>
        public ObservedConditionStateSnapshot ToSnapshot()
        {
            return new ObservedConditionStateSnapshot
            {
                conditionDefName = conditionDefName,
                conditionKey = conditionKey,
                scope = scope,
                mapUniqueId = mapUniqueId,
                subjectPawnId = subjectPawnId,
                firstObservedTick = firstObservedTick,
                lastObservedTick = lastObservedTick,
                firstMissingTick = firstMissingTick,
                lastSeenEvidenceDefName = lastSeenEvidenceDefName,
                lastSeenEvidenceLabel = lastSeenEvidenceLabel,
                lastSeenEvidenceCount = lastSeenEvidenceCount,
                startRecorded = startRecorded,
                endRecorded = endRecorded
            };
        }

        /// <summary>Copies the policy's post-decision snapshot back onto this saved row.</summary>
        public void CopyFrom(ObservedConditionStateSnapshot snapshot)
        {
            if (snapshot == null)
            {
                return;
            }

            conditionDefName = snapshot.conditionDefName;
            conditionKey = snapshot.conditionKey;
            scope = snapshot.scope;
            mapUniqueId = snapshot.mapUniqueId;
            subjectPawnId = snapshot.subjectPawnId;
            firstObservedTick = snapshot.firstObservedTick;
            lastObservedTick = snapshot.lastObservedTick;
            firstMissingTick = snapshot.firstMissingTick;
            lastSeenEvidenceDefName = snapshot.lastSeenEvidenceDefName;
            lastSeenEvidenceLabel = snapshot.lastSeenEvidenceLabel;
            lastSeenEvidenceCount = snapshot.lastSeenEvidenceCount;
            startRecorded = snapshot.startRecorded;
            endRecorded = snapshot.endRecorded;
        }

        /// <summary>Creates a fresh saved row from a policy snapshot (used when a condition starts).</summary>
        public static ActiveObservedConditionState FromSnapshot(ObservedConditionStateSnapshot snapshot)
        {
            ActiveObservedConditionState state = new ActiveObservedConditionState();
            state.CopyFrom(snapshot);
            return state;
        }
    }
}
