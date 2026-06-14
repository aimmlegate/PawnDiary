// One pawn's diary index: the ordered event IDs they appear in, plus legacy inline
// entries from older saves. Pure data + save/load. Split out of DiaryGameComponent.cs.
using System.Collections.Generic;
using Verse;

namespace PawnDiary
{
    /// <summary>
    /// Tracks one pawn's diary index: which events they appear in, which persona
    /// drives their LLM output, and whether generation is enabled for that pawn.
    /// Persisted via RimWorld's save/load system (IExposable).
    /// </summary>
    public class PawnDiaryRecord : IExposable
    {
        // RimWorld unique load ID — the canonical cross-save reference for this pawn.
        public string pawnId;

        // Display name cached at save time, for UI when the pawn isn't loaded.
        public string pawnName;

        // Pawn-specific generation controls. These live on the diary record because they need
        // to survive saves and are edited from the pawn's own inspector tab.
        // Which DiaryPersonaDef this pawn uses for LLM prompts.
        public string personaDefName;

        // Per-pawn toggle: when false, this pawn is skipped during diary generation.
        public bool diaryGenerationEnabled = true;

        // Ordered list of DiaryEvent IDs this pawn appears in.
        public List<string> eventIds = new List<string>();

        // Legacy inline entries from older saves (pre-event model).
        public List<DiaryEntry> entries = new List<DiaryEntry>();

        /// <summary>
        /// Serialises/deserialises this record into the RimWorld save file.
        /// PostLoadInit back-fills older saves and recovers gracefully if a
        /// persona Def was renamed or removed.
        /// </summary>
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
