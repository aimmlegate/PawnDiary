// Verse Scribe adapter for one visible-only creepjoiner continuity row. The pure snapshot and all
// corrupt/future-state normalization live under Source/Capture; the same seven frozen primitive keys
// now cover joined, non-terminal surgical disclosure, and terminal outcomes without ever retaining a
// tracker, Pawn, Def, worker, trigger time, or hidden outcome.
using PawnDiary.Capture;
using Verse;

namespace PawnDiary
{
    /// <summary>Deep-scribed visible creepjoiner arrival/outcome continuity.</summary>
    internal sealed class CreepJoinerArcState : IExposable
    {
        public string pawnId = string.Empty;
        public string arrivalEventId = string.Empty;
        public int joinedTick;
        public string lastVisiblePhase = string.Empty;
        public string lastVisibleEventId = string.Empty;
        public bool terminal;
        public int schemaVersion;

        /// <summary>Reads/writes the unchanged seven-field A2.0/A2.1 primitive schema.</summary>
        public void ExposeData()
        {
            Scribe_Values.Look(ref pawnId, "pawnId");
            Scribe_Values.Look(ref arrivalEventId, "arrivalEventId");
            Scribe_Values.Look(ref joinedTick, "joinedTick", 0);
            Scribe_Values.Look(ref lastVisiblePhase, "lastVisiblePhase");
            Scribe_Values.Look(ref lastVisibleEventId, "lastVisibleEventId");
            Scribe_Values.Look(ref terminal, "terminal", false);
            Scribe_Values.Look(ref schemaVersion, "schemaVersion", 0);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                pawnId = pawnId ?? string.Empty;
                arrivalEventId = arrivalEventId ?? string.Empty;
                lastVisiblePhase = lastVisiblePhase ?? string.Empty;
                lastVisibleEventId = lastVisibleEventId ?? string.Empty;
            }
        }

        internal CreepJoinerArcSnapshot ToSnapshot()
        {
            return new CreepJoinerArcSnapshot
            {
                pawnId = pawnId ?? string.Empty,
                arrivalEventId = arrivalEventId ?? string.Empty,
                joinedTick = joinedTick,
                lastVisiblePhase = lastVisiblePhase ?? string.Empty,
                lastVisibleEventId = lastVisibleEventId ?? string.Empty,
                terminal = terminal,
                schemaVersion = schemaVersion
            };
        }

        internal static CreepJoinerArcState FromSnapshot(CreepJoinerArcSnapshot source)
        {
            if (source == null) return null;
            return new CreepJoinerArcState
            {
                pawnId = source.pawnId ?? string.Empty,
                arrivalEventId = source.arrivalEventId ?? string.Empty,
                joinedTick = source.joinedTick,
                lastVisiblePhase = source.lastVisiblePhase ?? string.Empty,
                lastVisibleEventId = source.lastVisibleEventId ?? string.Empty,
                terminal = source.terminal,
                schemaVersion = source.schemaVersion
            };
        }
    }
}
