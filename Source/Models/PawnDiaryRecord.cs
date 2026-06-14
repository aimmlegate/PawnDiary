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

        // Pawn-specific generation controls. These live on the diary record because they need
        // to survive saves and are edited from the pawn's own inspector tab.
        public string personaDefName;
        public bool diaryGenerationEnabled = true;

        public List<string> eventIds = new List<string>();
        public List<DiaryEntry> entries = new List<DiaryEntry>();

        public void ExposeData()
        {
            Scribe_Values.Look(ref pawnId, "pawnId");
            Scribe_Values.Look(ref pawnName, "pawnName");
            Scribe_Values.Look(ref personaDefName, "personaDefName", DiaryPersonas.Default.defName);
            Scribe_Values.Look(ref diaryGenerationEnabled, "diaryGenerationEnabled", true);
            Scribe_Collections.Look(ref eventIds, "eventIds", LookMode.Value);
            Scribe_Collections.Look(ref entries, "entries", LookMode.Deep);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                // Back-fill older saves and recover gracefully if a persona Def was renamed/removed.
                if (string.IsNullOrWhiteSpace(personaDefName) || DiaryPersonas.ForDefName(personaDefName) == null)
                {
                    personaDefName = DiaryPersonas.Default.defName;
                }

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
