// Observed conditions, part 1 of the pure layer: a single live observation produced by one scan.
//
// Where event windows (DiaryEventWindowDef) react to one-shot *signals* and then guess how long the
// situation lasts via a fixed timeout, observed conditions are re-derived from *live* game state on
// every scan: the scanner reads cheap facts at the edge (map danger, active game conditions, spawned
// evidence things, pawn hediffs) and emits one of these plain DTOs per condition it currently sees.
// The pure policy then diffs these observations against the saved active state to decide what starts,
// refreshes, or ends. Keeping this a plain object (no RimWorld/Verse types) is what lets the policy be
// unit-tested without the game. New to C#/RimWorld? See AGENTS.md.
namespace PawnDiary
{
    /// <summary>
    /// What a condition is scoped to. Map conditions belong to one map (a raid on the home map); Pawn
    /// conditions belong to one subject pawn (a visible hediff); Colony conditions are global and not
    /// tied to a single map. The scope is part of a condition's identity, so the same Def can stay
    /// active independently on two different maps or for two different pawns.
    /// </summary>
    public enum ObservedConditionScope
    {
        Map,
        Pawn,
        Colony
    }

    /// <summary>
    /// One condition the scanner currently observes in live game state. The scanner fills the identity
    /// fields (which Def, which map/pawn) plus optional "evidence" describing the concrete thing that
    /// proves the condition (for example a gray-flesh sample), which later colors the diary page text.
    /// </summary>
    public sealed class ObservedConditionObservation
    {
        // Identity: which Def saw this, and the stable key it shares across renames.
        public string conditionDefName;
        public string conditionKey;
        public ObservedConditionScope scope;

        // Identity: a Map condition carries a map id; a Pawn condition carries the subject pawn id.
        // Colony conditions leave both at their neutral values (mapUniqueId -1, subjectPawnId empty).
        public int mapUniqueId = -1;
        public string subjectPawnId;

        // Optional observable evidence: the concrete defName/label the scanner saw and a count of how
        // many. Evidence is what the pawn could actually notice, never a hidden game mechanic.
        public string evidenceDefName;
        public string evidenceLabel;
        public int evidenceCount;
    }
}
