// One pawn's diary index: the ordered event IDs they appear in, plus legacy inline
// entries from older saves. Pure data + save/load. Split out of DiaryGameComponent.cs.
using System.Collections.Generic;
using Verse;

namespace PawnDiary
{
    public class PawnDiaryRecord : IExposable
    {
        public string pawnId;
        public string pawnName;
        public List<string> eventIds = new List<string>();
        public List<DiaryEntry> entries = new List<DiaryEntry>();

        public void ExposeData()
        {
            Scribe_Values.Look(ref pawnId, "pawnId");
            Scribe_Values.Look(ref pawnName, "pawnName");
            Scribe_Collections.Look(ref eventIds, "eventIds", LookMode.Value);
            Scribe_Collections.Look(ref entries, "entries", LookMode.Deep);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (eventIds == null)
                {
                    eventIds = new List<string>();
                }

                if (entries == null)
                {
                    entries = new List<DiaryEntry>();
                }
            }
        }
    }
}
