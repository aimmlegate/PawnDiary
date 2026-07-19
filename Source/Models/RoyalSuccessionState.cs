// Saved Royalty Phase-5 succession facts. Only fully committed, detached identity/title facts are
// serialized; active candidates and live Pawns/Factions/Defs remain transient.
using Verse;

namespace PawnDiary
{
    /// <summary>Deep-scribed committed succession row retained only through its bounded claim window.</summary>
    public sealed class RoyalSuccessionState : IExposable
    {
        public string correlationId = string.Empty;
        public string deceasedPawnId = string.Empty;
        public string deceasedPawnName = string.Empty;
        public string heirPawnId = string.Empty;
        public string heirPawnName = string.Empty;
        public string factionId = string.Empty;
        public string factionName = string.Empty;
        public string inheritedTitleDefName = string.Empty;
        public string inheritedTitleLabel = string.Empty;
        public int inheritedTitleSeniority;
        public string previousHeirTitleDefName = string.Empty;
        public string previousHeirTitleLabel = string.Empty;
        public int previousHeirTitleSeniority = -1;
        public int candidateTick;
        public int commitTick;
        public int expiresTick;
        public bool pageClaimed;
        public bool titleMutationClaimed;

        public void ExposeData()
        {
            Scribe_Values.Look(ref correlationId, "correlationId");
            Scribe_Values.Look(ref deceasedPawnId, "deceasedPawnId");
            Scribe_Values.Look(ref deceasedPawnName, "deceasedPawnName");
            Scribe_Values.Look(ref heirPawnId, "heirPawnId");
            Scribe_Values.Look(ref heirPawnName, "heirPawnName");
            Scribe_Values.Look(ref factionId, "factionId");
            Scribe_Values.Look(ref factionName, "factionName");
            Scribe_Values.Look(ref inheritedTitleDefName, "inheritedTitleDefName");
            Scribe_Values.Look(ref inheritedTitleLabel, "inheritedTitleLabel");
            Scribe_Values.Look(ref inheritedTitleSeniority, "inheritedTitleSeniority", 0);
            Scribe_Values.Look(ref previousHeirTitleDefName, "previousHeirTitleDefName");
            Scribe_Values.Look(ref previousHeirTitleLabel, "previousHeirTitleLabel");
            Scribe_Values.Look(ref previousHeirTitleSeniority, "previousHeirTitleSeniority", -1);
            Scribe_Values.Look(ref candidateTick, "candidateTick", 0);
            Scribe_Values.Look(ref commitTick, "commitTick", 0);
            Scribe_Values.Look(ref expiresTick, "expiresTick", 0);
            Scribe_Values.Look(ref pageClaimed, "pageClaimed", false);
            Scribe_Values.Look(ref titleMutationClaimed, "titleMutationClaimed", false);
        }

        internal RoyalSuccessionFact ToSnapshot()
        {
            return new RoyalSuccessionFact
            {
                correlationId = correlationId, deceasedPawnId = deceasedPawnId,
                deceasedPawnName = deceasedPawnName, heirPawnId = heirPawnId,
                heirPawnName = heirPawnName, factionId = factionId, factionName = factionName,
                inheritedTitleDefName = inheritedTitleDefName, inheritedTitleLabel = inheritedTitleLabel,
                inheritedTitleSeniority = inheritedTitleSeniority,
                previousHeirTitleDefName = previousHeirTitleDefName,
                previousHeirTitleLabel = previousHeirTitleLabel,
                previousHeirTitleSeniority = previousHeirTitleSeniority,
                candidateTick = candidateTick, commitTick = commitTick, expiresTick = expiresTick,
                pageClaimed = pageClaimed, titleMutationClaimed = titleMutationClaimed
            };
        }

        internal static RoyalSuccessionState FromSnapshot(RoyalSuccessionFact value)
        {
            return value == null ? null : new RoyalSuccessionState
            {
                correlationId = value.correlationId, deceasedPawnId = value.deceasedPawnId,
                deceasedPawnName = value.deceasedPawnName, heirPawnId = value.heirPawnId,
                heirPawnName = value.heirPawnName, factionId = value.factionId,
                factionName = value.factionName, inheritedTitleDefName = value.inheritedTitleDefName,
                inheritedTitleLabel = value.inheritedTitleLabel,
                inheritedTitleSeniority = value.inheritedTitleSeniority,
                previousHeirTitleDefName = value.previousHeirTitleDefName,
                previousHeirTitleLabel = value.previousHeirTitleLabel,
                previousHeirTitleSeniority = value.previousHeirTitleSeniority,
                candidateTick = value.candidateTick, commitTick = value.commitTick,
                expiresTick = value.expiresTick, pageClaimed = value.pageClaimed,
                titleMutationClaimed = value.titleMutationClaimed
            };
        }
    }
}
