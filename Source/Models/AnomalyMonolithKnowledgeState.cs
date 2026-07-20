// Verse Scribe adapter for one detached Anomaly monolith-study snapshot. The pure snapshot and
// normalization live under Source/Capture; this small model owns only stable save tokens and never
// stores a live Pawn, monolith, study comp, Def, or letter.
//
// New to C#/RimWorld? See AGENTS.md ("IExposable").
using PawnDiary.Capture;
using Verse;

namespace PawnDiary
{
    /// <summary>Deep-scribed event-time knowledge which may enrich one later monolith activation.</summary>
    internal sealed class AnomalyMonolithKnowledgeState : IExposable
    {
        public string researcherPawnId = string.Empty;
        public string studyStage = string.Empty;
        public int tick = -1;
        public int reachedProgress;
        public bool becameActivatable;
        public bool consumed;

        /// <summary>Reads/writes only additive primitive fields.</summary>
        public void ExposeData()
        {
            Scribe_Values.Look(ref researcherPawnId, "researcherPawnId");
            Scribe_Values.Look(ref studyStage, "studyStage");
            Scribe_Values.Look(ref tick, "tick", -1);
            Scribe_Values.Look(ref reachedProgress, "reachedProgress", 0);
            Scribe_Values.Look(ref becameActivatable, "becameActivatable", false);
            Scribe_Values.Look(ref consumed, "consumed", false);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                researcherPawnId = researcherPawnId ?? string.Empty;
                studyStage = studyStage ?? string.Empty;
            }
        }

        /// <summary>Copies this Scribe model into the assembly-free policy contract.</summary>
        internal AnomalyMonolithKnowledgeSnapshot ToSnapshot()
        {
            return new AnomalyMonolithKnowledgeSnapshot
            {
                researcherPawnId = researcherPawnId ?? string.Empty,
                studyStage = studyStage ?? string.Empty,
                tick = tick,
                reachedProgress = reachedProgress,
                becameActivatable = becameActivatable,
                consumed = consumed
            };
        }

        /// <summary>Copies a detached normalized snapshot into a fresh Scribe model.</summary>
        internal static AnomalyMonolithKnowledgeState FromSnapshot(
            AnomalyMonolithKnowledgeSnapshot source)
        {
            if (source == null) return null;
            return new AnomalyMonolithKnowledgeState
            {
                researcherPawnId = source.researcherPawnId ?? string.Empty,
                studyStage = source.studyStage ?? string.Empty,
                tick = source.tick,
                reachedProgress = source.reachedProgress,
                becameActivatable = source.becameActivatable,
                consumed = source.consumed
            };
        }
    }
}
