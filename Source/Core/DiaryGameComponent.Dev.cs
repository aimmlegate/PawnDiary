// Dev-mode helper surface for Pawn Diary's debug action panel. These wrappers intentionally stay
// thin: the real capture, filtering, and generation behavior remains in the same signals and
// scanners used during ordinary gameplay.
using System;
using System.Collections.Generic;
using PawnDiary.Ingestion;
using Verse;

namespace PawnDiary
{
    public partial class DiaryGameComponent
    {
        internal const string DevPanelSectionEvents = "events";
        internal const string DevPanelSectionDiary = "diary";
        internal const string DevPanelSectionFixtures = "fixtures";
        internal const string DevPanelDefaultThoughtDefName = "AteWithoutTable";
        internal const string DevPanelDefaultInspirationDefName = "Inspired_Recruitment";
        internal const string DevPanelDefaultMentalStateDefName = "Berserk";
        internal const string DevPanelDefaultPairedMentalStateDefName = "SocialFighting";
        internal const string DevPanelDefaultTaleDefName = "Vomited";
        internal const string DevPanelDefaultHediffDefName = "Flu";
        internal const string DevPanelDefaultGameConditionDefName = "Aurora";
        internal const string DevPanelDefaultInteractionDefName = "DeepTalk";
        internal const string DevPanelDefaultRelationDefName = "Lover";
        internal const string DevPanelDefaultIncidentDefName = "RaidEnemy";
        internal const string DevPanelDefaultQuestScriptDefName = "WandererJoins";

        // Saved debug-panel UI state. These are stable primitive IDs/values only: never save live
        // Pawn, Window, or GUI objects. The panel can then survive closing/reopening and save/load
        // without coupling the game state to RimWorld UI instances.
        private string devPanelSelectedPawnId;
        private string devPanelSelectedPartnerId;
        private string devPanelSectionId = DevPanelSectionEvents;
        private float devPanelEventsScrollY;
        private float devPanelDiaryScrollY;
        private float devPanelFixturesScrollY;
        private bool devPanelFixtureSelectionInitialized;
        private List<string> devPanelSelectedFixtureIds = new List<string>();
        private string devPanelThoughtDefName = DevPanelDefaultThoughtDefName;
        private string devPanelInspirationDefName = DevPanelDefaultInspirationDefName;
        private string devPanelMentalStateDefName = DevPanelDefaultMentalStateDefName;
        private string devPanelPairedMentalStateDefName = DevPanelDefaultPairedMentalStateDefName;
        private string devPanelTaleDefName = DevPanelDefaultTaleDefName;
        private string devPanelHediffDefName = DevPanelDefaultHediffDefName;
        private string devPanelGameConditionDefName = DevPanelDefaultGameConditionDefName;
        private string devPanelInteractionDefName = DevPanelDefaultInteractionDefName;
        private string devPanelRelationDefName = DevPanelDefaultRelationDefName;
        private string devPanelIncidentDefName = DevPanelDefaultIncidentDefName;
        private string devPanelQuestScriptDefName = DevPanelDefaultQuestScriptDefName;
        private string devPanelAbilityDefName;

        /// <summary>
        /// Dev-only: submits the selected pawn's current work through the normal work signal.
        /// </summary>
        internal bool TriggerWorkSignalForDev(Pawn pawn)
        {
            return Dispatch(new WorkSignal(pawn));
        }

        /// <summary>
        /// Dev-only: runs the normal situational-thought progression scanner immediately.
        /// </summary>
        internal void ScanThoughtProgressionsForDev()
        {
            ScanThoughtProgressionsForDiaryEvents(false);
        }

        /// <summary>
        /// Dev-only: runs the normal end-of-day reflection builder for one pawn immediately.
        /// </summary>
        internal void FlushDaySummaryForDev(Pawn pawn)
        {
            FlushDaySummaryForPawn(pawn);
        }

        /// <summary>
        /// Dev-only: removes the selected pawn's compact archive rows without touching hot diary events.
        /// </summary>
        internal int PurgeArchivedEntriesForPawnForDev(Pawn pawn)
        {
            if (!Prefs.DevMode || pawn == null)
            {
                return 0;
            }

            string pawnId = pawn.GetUniqueLoadID();
            int removed = archive.RemoveForPawn(pawnId);
            if (removed > 0)
            {
                DiaryStateVersion.Bump();
            }

            return removed;
        }

        /// <summary>
        /// Saves and restores the debug event panel's remembered UI state with the current game.
        /// </summary>
        internal void ExposeDevPanelStateForDev()
        {
            Scribe_Values.Look(ref devPanelSelectedPawnId, "devPanelSelectedPawnId");
            Scribe_Values.Look(ref devPanelSelectedPartnerId, "devPanelSelectedPartnerId");
            Scribe_Values.Look(ref devPanelSectionId, "devPanelSectionId", DevPanelSectionEvents);
            Scribe_Values.Look(ref devPanelEventsScrollY, "devPanelEventsScrollY", 0f);
            Scribe_Values.Look(ref devPanelDiaryScrollY, "devPanelDiaryScrollY", 0f);
            Scribe_Values.Look(ref devPanelFixturesScrollY, "devPanelFixturesScrollY", 0f);
            Scribe_Values.Look(ref devPanelFixtureSelectionInitialized, "devPanelFixtureSelectionInitialized", false);
            Scribe_Collections.Look(
                ref devPanelSelectedFixtureIds,
                "devPanelSelectedFixtureIds",
                LookMode.Value);
            Scribe_Values.Look(ref devPanelThoughtDefName, "devPanelThoughtDefName", DevPanelDefaultThoughtDefName);
            Scribe_Values.Look(ref devPanelInspirationDefName, "devPanelInspirationDefName", DevPanelDefaultInspirationDefName);
            Scribe_Values.Look(ref devPanelMentalStateDefName, "devPanelMentalStateDefName", DevPanelDefaultMentalStateDefName);
            Scribe_Values.Look(ref devPanelPairedMentalStateDefName, "devPanelPairedMentalStateDefName", DevPanelDefaultPairedMentalStateDefName);
            Scribe_Values.Look(ref devPanelTaleDefName, "devPanelTaleDefName", DevPanelDefaultTaleDefName);
            Scribe_Values.Look(ref devPanelHediffDefName, "devPanelHediffDefName", DevPanelDefaultHediffDefName);
            Scribe_Values.Look(ref devPanelGameConditionDefName, "devPanelGameConditionDefName", DevPanelDefaultGameConditionDefName);
            Scribe_Values.Look(ref devPanelInteractionDefName, "devPanelInteractionDefName", DevPanelDefaultInteractionDefName);
            Scribe_Values.Look(ref devPanelRelationDefName, "devPanelRelationDefName", DevPanelDefaultRelationDefName);
            Scribe_Values.Look(ref devPanelIncidentDefName, "devPanelIncidentDefName", DevPanelDefaultIncidentDefName);
            Scribe_Values.Look(ref devPanelQuestScriptDefName, "devPanelQuestScriptDefName", DevPanelDefaultQuestScriptDefName);
            Scribe_Values.Look(ref devPanelAbilityDefName, "devPanelAbilityDefName");

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                NormalizeDevPanelStateForDev();
            }
        }

        internal string DevPanelSelectedPawnIdForDev
        {
            get { return devPanelSelectedPawnId; }
        }

        internal string DevPanelSelectedPartnerIdForDev
        {
            get { return devPanelSelectedPartnerId; }
        }

        internal string DevPanelSectionForDev
        {
            get
            {
                devPanelSectionId = NormalizeDevPanelSectionId(devPanelSectionId);
                return devPanelSectionId;
            }
        }

        internal string DevPanelThoughtDefNameForDev
        {
            get { return OrDefaultDevPanelId(devPanelThoughtDefName, DevPanelDefaultThoughtDefName); }
        }

        internal string DevPanelInspirationDefNameForDev
        {
            get { return OrDefaultDevPanelId(devPanelInspirationDefName, DevPanelDefaultInspirationDefName); }
        }

        internal string DevPanelMentalStateDefNameForDev
        {
            get { return OrDefaultDevPanelId(devPanelMentalStateDefName, DevPanelDefaultMentalStateDefName); }
        }

        internal string DevPanelPairedMentalStateDefNameForDev
        {
            get { return OrDefaultDevPanelId(devPanelPairedMentalStateDefName, DevPanelDefaultPairedMentalStateDefName); }
        }

        internal string DevPanelTaleDefNameForDev
        {
            get { return OrDefaultDevPanelId(devPanelTaleDefName, DevPanelDefaultTaleDefName); }
        }

        internal string DevPanelHediffDefNameForDev
        {
            get { return OrDefaultDevPanelId(devPanelHediffDefName, DevPanelDefaultHediffDefName); }
        }

        internal string DevPanelGameConditionDefNameForDev
        {
            get { return OrDefaultDevPanelId(devPanelGameConditionDefName, DevPanelDefaultGameConditionDefName); }
        }

        internal string DevPanelInteractionDefNameForDev
        {
            get { return OrDefaultDevPanelId(devPanelInteractionDefName, DevPanelDefaultInteractionDefName); }
        }

        internal string DevPanelRelationDefNameForDev
        {
            get { return OrDefaultDevPanelId(devPanelRelationDefName, DevPanelDefaultRelationDefName); }
        }

        internal string DevPanelIncidentDefNameForDev
        {
            get { return OrDefaultDevPanelId(devPanelIncidentDefName, DevPanelDefaultIncidentDefName); }
        }

        internal string DevPanelQuestScriptDefNameForDev
        {
            get { return OrDefaultDevPanelId(devPanelQuestScriptDefName, DevPanelDefaultQuestScriptDefName); }
        }

        internal string DevPanelAbilityDefNameForDev
        {
            get { return CleanDevPanelId(devPanelAbilityDefName); }
        }

        internal void SetDevPanelSelectedPawnForDev(string pawnId)
        {
            devPanelSelectedPawnId = CleanDevPanelId(pawnId);
            if (string.Equals(devPanelSelectedPawnId, devPanelSelectedPartnerId, StringComparison.Ordinal))
            {
                devPanelSelectedPartnerId = null;
            }
        }

        internal void SetDevPanelSelectedPartnerForDev(string pawnId)
        {
            devPanelSelectedPartnerId = CleanDevPanelId(pawnId);
            if (string.Equals(devPanelSelectedPawnId, devPanelSelectedPartnerId, StringComparison.Ordinal))
            {
                devPanelSelectedPartnerId = null;
            }
        }

        internal void SetDevPanelSectionForDev(string sectionId)
        {
            devPanelSectionId = NormalizeDevPanelSectionId(sectionId);
        }

        internal void SetDevPanelThoughtDefNameForDev(string defName)
        {
            devPanelThoughtDefName = OrDefaultDevPanelId(defName, DevPanelDefaultThoughtDefName);
        }

        internal void SetDevPanelInspirationDefNameForDev(string defName)
        {
            devPanelInspirationDefName = OrDefaultDevPanelId(defName, DevPanelDefaultInspirationDefName);
        }

        internal void SetDevPanelMentalStateDefNameForDev(string defName)
        {
            devPanelMentalStateDefName = OrDefaultDevPanelId(defName, DevPanelDefaultMentalStateDefName);
        }

        internal void SetDevPanelPairedMentalStateDefNameForDev(string defName)
        {
            devPanelPairedMentalStateDefName = OrDefaultDevPanelId(defName, DevPanelDefaultPairedMentalStateDefName);
        }

        internal void SetDevPanelTaleDefNameForDev(string defName)
        {
            devPanelTaleDefName = OrDefaultDevPanelId(defName, DevPanelDefaultTaleDefName);
        }

        internal void SetDevPanelHediffDefNameForDev(string defName)
        {
            devPanelHediffDefName = OrDefaultDevPanelId(defName, DevPanelDefaultHediffDefName);
        }

        internal void SetDevPanelGameConditionDefNameForDev(string defName)
        {
            devPanelGameConditionDefName = OrDefaultDevPanelId(defName, DevPanelDefaultGameConditionDefName);
        }

        internal void SetDevPanelInteractionDefNameForDev(string defName)
        {
            devPanelInteractionDefName = OrDefaultDevPanelId(defName, DevPanelDefaultInteractionDefName);
        }

        internal void SetDevPanelRelationDefNameForDev(string defName)
        {
            devPanelRelationDefName = OrDefaultDevPanelId(defName, DevPanelDefaultRelationDefName);
        }

        internal void SetDevPanelIncidentDefNameForDev(string defName)
        {
            devPanelIncidentDefName = OrDefaultDevPanelId(defName, DevPanelDefaultIncidentDefName);
        }

        internal void SetDevPanelQuestScriptDefNameForDev(string defName)
        {
            devPanelQuestScriptDefName = OrDefaultDevPanelId(defName, DevPanelDefaultQuestScriptDefName);
        }

        internal void SetDevPanelAbilityDefNameForDev(string defName)
        {
            devPanelAbilityDefName = CleanDevPanelId(defName);
        }

        internal void ResetDevPanelTriggerDefsForDev()
        {
            devPanelThoughtDefName = DevPanelDefaultThoughtDefName;
            devPanelInspirationDefName = DevPanelDefaultInspirationDefName;
            devPanelMentalStateDefName = DevPanelDefaultMentalStateDefName;
            devPanelPairedMentalStateDefName = DevPanelDefaultPairedMentalStateDefName;
            devPanelTaleDefName = DevPanelDefaultTaleDefName;
            devPanelHediffDefName = DevPanelDefaultHediffDefName;
            devPanelGameConditionDefName = DevPanelDefaultGameConditionDefName;
            devPanelInteractionDefName = DevPanelDefaultInteractionDefName;
            devPanelRelationDefName = DevPanelDefaultRelationDefName;
            devPanelIncidentDefName = DevPanelDefaultIncidentDefName;
            devPanelQuestScriptDefName = DevPanelDefaultQuestScriptDefName;
            devPanelAbilityDefName = null;
        }

        internal float DevPanelScrollYForDev(string sectionId)
        {
            sectionId = NormalizeDevPanelSectionId(sectionId);
            if (sectionId == DevPanelSectionDiary)
            {
                return devPanelDiaryScrollY;
            }

            if (sectionId == DevPanelSectionFixtures)
            {
                return devPanelFixturesScrollY;
            }

            return devPanelEventsScrollY;
        }

        internal void SetDevPanelScrollYForDev(string sectionId, float value)
        {
            float scrollY = SanitizeDevPanelScroll(value);
            sectionId = NormalizeDevPanelSectionId(sectionId);
            if (sectionId == DevPanelSectionDiary)
            {
                devPanelDiaryScrollY = scrollY;
                return;
            }

            if (sectionId == DevPanelSectionFixtures)
            {
                devPanelFixturesScrollY = scrollY;
                return;
            }

            devPanelEventsScrollY = scrollY;
        }

        internal void EnsureDevPanelFixtureSelectionForDev()
        {
            if (!devPanelFixtureSelectionInitialized)
            {
                SelectAllDevPanelFixturesForDev();
                return;
            }

            NormalizeDevPanelFixtureSelection();
        }

        internal bool DevPanelFixtureSelectedForDev(string id)
        {
            EnsureDevPanelFixtureSelectionForDev();
            return !string.IsNullOrWhiteSpace(id) && devPanelSelectedFixtureIds.Contains(id);
        }

        internal void SetDevPanelFixtureSelectedForDev(string id, bool selected)
        {
            devPanelFixtureSelectionInitialized = true;
            id = CleanDevPanelId(id);
            if (id == null)
            {
                return;
            }

            if (devPanelSelectedFixtureIds == null)
            {
                devPanelSelectedFixtureIds = new List<string>();
            }

            bool contained = devPanelSelectedFixtureIds.Contains(id);
            if (selected && !contained)
            {
                devPanelSelectedFixtureIds.Add(id);
            }
            else if (!selected && contained)
            {
                devPanelSelectedFixtureIds.Remove(id);
            }
        }

        internal void SelectAllDevPanelFixturesForDev()
        {
            devPanelFixtureSelectionInitialized = true;
            if (devPanelSelectedFixtureIds == null)
            {
                devPanelSelectedFixtureIds = new List<string>();
            }

            devPanelSelectedFixtureIds.Clear();
            IReadOnlyList<DevPromptSuiteEntry> entries = AllSuiteEntries;
            for (int i = 0; i < entries.Count; i++)
            {
                string id = CleanDevPanelId(entries[i]?.id);
                if (id != null)
                {
                    devPanelSelectedFixtureIds.Add(id);
                }
            }
        }

        internal void ClearDevPanelFixturesForDev()
        {
            devPanelFixtureSelectionInitialized = true;
            if (devPanelSelectedFixtureIds == null)
            {
                devPanelSelectedFixtureIds = new List<string>();
                return;
            }

            devPanelSelectedFixtureIds.Clear();
        }

        internal int DevPanelSelectedFixtureCountForDev(IReadOnlyList<DevPromptSuiteEntry> entries)
        {
            EnsureDevPanelFixtureSelectionForDev();
            int count = 0;
            for (int i = 0; i < entries.Count; i++)
            {
                DevPromptSuiteEntry entry = entries[i];
                if (entry != null && devPanelSelectedFixtureIds.Contains(entry.id))
                {
                    count++;
                }
            }

            return count;
        }

        internal List<DevPromptSuiteEntry> DevPanelSelectedFixturesForDev(IReadOnlyList<DevPromptSuiteEntry> entries)
        {
            EnsureDevPanelFixtureSelectionForDev();
            List<DevPromptSuiteEntry> result = new List<DevPromptSuiteEntry>();
            for (int i = 0; i < entries.Count; i++)
            {
                DevPromptSuiteEntry entry = entries[i];
                if (entry != null && devPanelSelectedFixtureIds.Contains(entry.id))
                {
                    result.Add(entry);
                }
            }

            return result;
        }

        private void NormalizeDevPanelStateForDev()
        {
            devPanelSelectedPawnId = CleanDevPanelId(devPanelSelectedPawnId);
            devPanelSelectedPartnerId = CleanDevPanelId(devPanelSelectedPartnerId);
            devPanelSectionId = NormalizeDevPanelSectionId(devPanelSectionId);
            devPanelEventsScrollY = SanitizeDevPanelScroll(devPanelEventsScrollY);
            devPanelDiaryScrollY = SanitizeDevPanelScroll(devPanelDiaryScrollY);
            devPanelFixturesScrollY = SanitizeDevPanelScroll(devPanelFixturesScrollY);
            devPanelThoughtDefName = OrDefaultDevPanelId(devPanelThoughtDefName, DevPanelDefaultThoughtDefName);
            devPanelInspirationDefName = OrDefaultDevPanelId(devPanelInspirationDefName, DevPanelDefaultInspirationDefName);
            devPanelMentalStateDefName = OrDefaultDevPanelId(devPanelMentalStateDefName, DevPanelDefaultMentalStateDefName);
            devPanelPairedMentalStateDefName = OrDefaultDevPanelId(devPanelPairedMentalStateDefName, DevPanelDefaultPairedMentalStateDefName);
            devPanelTaleDefName = OrDefaultDevPanelId(devPanelTaleDefName, DevPanelDefaultTaleDefName);
            devPanelHediffDefName = OrDefaultDevPanelId(devPanelHediffDefName, DevPanelDefaultHediffDefName);
            devPanelGameConditionDefName = OrDefaultDevPanelId(devPanelGameConditionDefName, DevPanelDefaultGameConditionDefName);
            devPanelInteractionDefName = OrDefaultDevPanelId(devPanelInteractionDefName, DevPanelDefaultInteractionDefName);
            devPanelRelationDefName = OrDefaultDevPanelId(devPanelRelationDefName, DevPanelDefaultRelationDefName);
            devPanelIncidentDefName = OrDefaultDevPanelId(devPanelIncidentDefName, DevPanelDefaultIncidentDefName);
            devPanelQuestScriptDefName = OrDefaultDevPanelId(devPanelQuestScriptDefName, DevPanelDefaultQuestScriptDefName);
            devPanelAbilityDefName = CleanDevPanelId(devPanelAbilityDefName);
            NormalizeDevPanelFixtureSelection();
        }

        private void NormalizeDevPanelFixtureSelection()
        {
            if (devPanelSelectedFixtureIds == null)
            {
                devPanelSelectedFixtureIds = new List<string>();
                return;
            }

            HashSet<string> seen = new HashSet<string>();
            for (int i = devPanelSelectedFixtureIds.Count - 1; i >= 0; i--)
            {
                string id = CleanDevPanelId(devPanelSelectedFixtureIds[i]);
                if (id == null || !IsKnownDevPanelFixtureId(id) || !seen.Add(id))
                {
                    devPanelSelectedFixtureIds.RemoveAt(i);
                }
                else
                {
                    devPanelSelectedFixtureIds[i] = id;
                }
            }
        }

        private static bool IsKnownDevPanelFixtureId(string id)
        {
            IReadOnlyList<DevPromptSuiteEntry> entries = AllSuiteEntries;
            for (int i = 0; i < entries.Count; i++)
            {
                if (string.Equals(entries[i]?.id, id, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static string NormalizeDevPanelSectionId(string sectionId)
        {
            if (string.Equals(sectionId, DevPanelSectionDiary, StringComparison.Ordinal))
            {
                return DevPanelSectionDiary;
            }

            if (string.Equals(sectionId, DevPanelSectionFixtures, StringComparison.Ordinal))
            {
                return DevPanelSectionFixtures;
            }

            return DevPanelSectionEvents;
        }

        private static string CleanDevPanelId(string id)
        {
            return string.IsNullOrWhiteSpace(id) ? null : id;
        }

        private static string OrDefaultDevPanelId(string id, string defaultId)
        {
            return CleanDevPanelId(id) ?? defaultId;
        }

        private static float SanitizeDevPanelScroll(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value) || value < 0f ? 0f : value;
        }
    }
}
