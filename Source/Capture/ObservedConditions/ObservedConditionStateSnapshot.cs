// Observed conditions, part 3 of the pure layer: a plain mirror of one saved ActiveObservedConditionState
// row. The saved model (Source/Models/ActiveObservedConditionState.cs) is an IExposable Verse type used
// for save/load; this snapshot is the Verse-free copy the pure policy reads and rewrites. The impure
// adapter converts saved rows <-> snapshots around each policy call. New to C#/RimWorld? See AGENTS.md.
namespace PawnDiary
{
    /// <summary>
    /// The remembered runtime state of one active observed condition, as seen by the pure policy. The
    /// policy never mutates an input snapshot; it clones one (<see cref="Clone"/>) and edits the copy,
    /// so callers can compare before/after and tests can assert on exact field transitions.
    /// </summary>
    internal sealed class ObservedConditionStateSnapshot
    {
        public string conditionDefName;
        public string conditionKey;
        public ObservedConditionScope scope;
        public int mapUniqueId = -1;
        public string subjectPawnId;

        // Lifecycle ticks. firstObservedTick anchors the start debounce; firstMissingTick (-1 while the
        // condition is still observed) anchors the end debounce once live state stops seeing it.
        public int firstObservedTick;
        public int lastObservedTick;
        public int firstMissingTick = -1;

        // Last observable evidence captured while the condition was seen, reused for the end page.
        public string lastSeenEvidenceDefName;
        public string lastSeenEvidenceLabel;
        public int lastSeenEvidenceCount;

        // Whether the start/end phases have already passed their debounce and been emitted, so the
        // policy never emits a duplicate start while a condition stays observed.
        public bool startRecorded;
        public bool endRecorded;

        /// <summary>
        /// The identity key that makes a condition independent per Def, scope, map, and subject pawn.
        /// Two raids on two maps, or a hediff on two pawns, are distinct conditions with distinct keys.
        /// </summary>
        public string IdentityKey()
        {
            return Identity(conditionKey, scope, mapUniqueId, subjectPawnId);
        }

        /// <summary>Builds the same identity key from raw parts (used for observations).</summary>
        public static string Identity(string conditionKey, ObservedConditionScope scope, int mapUniqueId,
            string subjectPawnId)
        {
            return (conditionKey ?? string.Empty)
                + "|" + (int)scope
                + "|" + mapUniqueId
                + "|" + (subjectPawnId ?? string.Empty);
        }

        /// <summary>Returns an independent copy so the policy can edit it without touching the input.</summary>
        public ObservedConditionStateSnapshot Clone()
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
    }
}
