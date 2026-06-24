// One pawn's diary index: the ordered event IDs they appear in, plus per-pawn writing style and
// generation controls. Pure data + save/load. Split out of DiaryGameComponent.cs.
using System.Collections.Generic;
using Verse;

namespace PawnDiary
{
    /// <summary>
    /// Tracks one pawn's diary index: which events they appear in, which writing style
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
        // Which writing-style Def this pawn uses for LLM prompts. The field name is legacy save data.
        public string personaDefName;

        // Per-pawn toggle: when false, this pawn is skipped during diary generation.
        public bool diaryGenerationEnabled = true;

        // Ordered list of DiaryEvent IDs this pawn appears in.
        public List<string> eventIds = new List<string>();

        /// <summary>
        /// Serialises/deserialises this record into the RimWorld save file.
        /// PostLoadInit keeps list fields non-null and recovers gracefully if a style Def was
        /// renamed or removed.
        /// </summary>
        public void ExposeData()
        {
            Scribe_Values.Look(ref pawnId, "pawnId");
            Scribe_Values.Look(ref pawnName, "pawnName");
            Scribe_Values.Look(ref personaDefName, "personaDefName", DiaryPersonas.Default.defName);
            Scribe_Values.Look(ref diaryGenerationEnabled, "diaryGenerationEnabled", true);
            Scribe_Collections.Look(ref eventIds, "eventIds", LookMode.Value);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                pawnId = pawnId ?? string.Empty;
                pawnName = pawnName ?? string.Empty;

                // Recover gracefully if a style Def was renamed/removed.
                if (string.IsNullOrWhiteSpace(personaDefName) || DiaryPersonas.ForDefName(personaDefName) == null)
                {
                    personaDefName = DiaryPersonas.Default.defName;
                }

                if (eventIds == null)
                {
                    eventIds = new List<string>();
                }
            }
        }
    }
}
