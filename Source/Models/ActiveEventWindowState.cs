// Saved runtime state for an XML event window. Defs describe what starts/ends a window; this
// model remembers which windows are currently active in the save.
using Verse;

namespace PawnDiary
{
    /// <summary>
    /// One active event window, persisted with the game so long-running threats survive save/load.
    /// </summary>
    public class ActiveEventWindowState : IExposable
    {
        public string windowDefName;
        public string windowKey;
        public int startedTick;
        public int expiresTick = -1;
        public int mapUniqueId = -1;
        public string startSource;
        public string startSignal;
        public string startDefName;
        public string startLabel;
        public string startSubjectPawnId;
        public string startSubjectLabel;

        public void ExposeData()
        {
            Scribe_Values.Look(ref windowDefName, "windowDefName");
            Scribe_Values.Look(ref windowKey, "windowKey");
            Scribe_Values.Look(ref startedTick, "startedTick");
            Scribe_Values.Look(ref expiresTick, "expiresTick", -1);
            Scribe_Values.Look(ref mapUniqueId, "mapUniqueId", -1);
            Scribe_Values.Look(ref startSource, "startSource");
            Scribe_Values.Look(ref startSignal, "startSignal");
            Scribe_Values.Look(ref startDefName, "startDefName");
            Scribe_Values.Look(ref startLabel, "startLabel");
            Scribe_Values.Look(ref startSubjectPawnId, "startSubjectPawnId");
            Scribe_Values.Look(ref startSubjectLabel, "startSubjectLabel");

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                windowDefName = windowDefName ?? string.Empty;
                windowKey = windowKey ?? string.Empty;
                startSource = startSource ?? string.Empty;
                startSignal = startSignal ?? string.Empty;
                startDefName = startDefName ?? string.Empty;
                startLabel = startLabel ?? string.Empty;
                startSubjectPawnId = startSubjectPawnId ?? string.Empty;
                startSubjectLabel = startSubjectLabel ?? string.Empty;
            }
        }
    }
}
