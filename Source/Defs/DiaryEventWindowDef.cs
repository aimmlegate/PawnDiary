// Event-window defs: XML-controlled narrative windows that start on one generic game signal,
// stay active for prompt context, then end on another signal or a timeout. These are deliberately
// source-agnostic so compatibility packs can watch incidents, quest lifecycle changes, spawned
// things, or future signal sources without new C# per event.
using System.Collections.Generic;
using Verse;

namespace PawnDiary
{
    /// <summary>
    /// Who receives the phase diary entry created by an event window. Map keeps the original
    /// colony-wide behavior; SubjectPawn is for signals such as letters that name one pawn.
    /// </summary>
    public enum EventWindowRecordScope
    {
        Map,
        SubjectPawn
    }

    /// <summary>
    /// One XML trigger that can start or end a <see cref="DiaryEventWindowDef"/>.
    /// </summary>
    public class DiaryEventWindowTriggerDef
    {
        // Examples: "Incident", "Quest", "ThingSpawned". Blank means any source.
        public string source;
        // Examples: "executed", "accepted", "completed", "failed", "spawned". Blank means any signal.
        public string signal;
        // Exact defName matches. These stay as plain strings for DLC/mod safety.
        public List<string> matchDefNames = new List<string>();
        // Case-insensitive substring matches over source/signal/defName/label for broad modded packs.
        public List<string> matchTokens = new List<string>();

        /// <summary>
        /// Copies the XML trigger into the pure matcher contract.
        /// </summary>
        public EventWindowTriggerRule ToRule()
        {
            return new EventWindowTriggerRule
            {
                source = source,
                signal = signal,
                matchDefNames = matchDefNames ?? new List<string>(),
                matchTokens = matchTokens ?? new List<string>()
            };
        }
    }

    /// <summary>
    /// XML-owned policy for one active event window.
    /// </summary>
    public class DiaryEventWindowDef : Def
    {
        public bool enabled = true;
        public string windowKey;
        public List<DiaryEventWindowTriggerDef> startSignals = new List<DiaryEventWindowTriggerDef>();
        public List<DiaryEventWindowTriggerDef> endSignals = new List<DiaryEventWindowTriggerDef>();

        // -1 means no timeout. Positive values are game ticks; 60000 ticks is one RimWorld day.
        public int timeoutTicks = -1;
        public int dedupTicks = 2500;
        public bool restartOnStart = false;

        public bool recordStartEvent = true;
        public bool recordEndEvent = true;
        public bool recordEndWithoutActive = true;
        public bool recordTimeoutEvent = true;
        public bool keepActive = true;
        public EventWindowRecordScope recordScope = EventWindowRecordScope.Map;

        public string startTextKey;
        public string endTextKey;
        public string timeoutTextKey;
        public string instruction;
        public string colorCue;

        public bool promptEnabled = true;
        public float promptWeight = 1f;
        public float normalPromptWeightMultiplier = 1f;
        public string promptPriorityKey;
        public string promptConditionKey;
        public string promptDescriptionKey;
        public List<string> promptCueKeys = new List<string>();

        /// <summary>
        /// Stable active-window key. XML can override it to share state across renamed defs.
        /// </summary>
        public string EffectiveWindowKey()
        {
            return string.IsNullOrWhiteSpace(windowKey) ? defName : windowKey;
        }

        public int EffectiveDedupTicks()
        {
            return dedupTicks < 0 ? 0 : dedupTicks;
        }
    }
}
