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

        // Legacy unread-count baseline from older saves. Current builds use hasUnreadGeneratedEntry
        // below so the inspect command never has to count historical pages during pawn selection.
        public int acknowledgedGeneratedEntryCount;

        // Cheap badge flag: set when a new main diary page finishes generation, cleared when this
        // pawn's Diary tab opens. This avoids scanning or counting saved entries just to draw a gizmo.
        public bool hasUnreadGeneratedEntry;

        // Ordered list of DiaryEvent IDs this pawn appears in.
        public List<string> eventIds = new List<string>();

        // Scanner bookkeeping for pawn progression pages. Additive save key; old saves create a
        // baseline-pending state so they do not emit catch-up milestone pages on first load.
        public PawnProgressionState progressionState;

        // Rare life-arc reflection cadence and memory repetition control. This is scheduling state,
        // not a separate pawn-history store; existing diary pages remain the history layer.
        public PawnArcScheduleState arcSchedule;

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
            Scribe_Values.Look(ref acknowledgedGeneratedEntryCount, "acknowledgedGeneratedEntryCount", -1);
            Scribe_Values.Look(ref hasUnreadGeneratedEntry, "hasUnreadGeneratedEntry", false);
            Scribe_Collections.Look(ref eventIds, "eventIds", LookMode.Value);
            Scribe_Deep.Look(ref progressionState, "progressionState");
            Scribe_Deep.Look(ref arcSchedule, "arcSchedule");

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

                EnsureProgressionState();
                EnsureArcSchedule();
            }
        }

        public PawnProgressionState EnsureProgressionState()
        {
            if (progressionState == null)
            {
                progressionState = new PawnProgressionState();
            }

            progressionState.Normalize();
            return progressionState;
        }

        public PawnArcScheduleState EnsureArcSchedule()
        {
            if (arcSchedule == null)
            {
                arcSchedule = new PawnArcScheduleState();
            }

            arcSchedule.Normalize(PawnArcScheduleState.DefaultRecentMemoryCap);
            return arcSchedule;
        }
    }
}
