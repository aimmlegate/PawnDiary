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
        // Literal settings overrides for the same phase text. Blank means use the Keyed XML value.
        // Placeholders mirror the Keyed strings: {0}=pawn label, {1}=signal/window label.
        public string startText;
        public string endText;
        public string timeoutText;
        public string instruction;
        public string colorCue;

        public bool promptEnabled = true;
        public float promptWeight = 1f;
        public float normalPromptWeightMultiplier = 1f;
        // Optional fade for active windows that keep influencing prompts. 0 ticks means no decay;
        // otherwise the prompt weight and any normal-context override move toward this minimum.
        public int promptDecayTicks = 0;
        public float promptDecayMinMultiplier = 1f;
        public string promptPriorityKey;
        public string promptConditionKey;
        public string promptDescriptionKey;
        public List<string> promptCueKeys = new List<string>();
        // Literal prompt-bias overrides. Blank/empty means use the Keyed XML defaults above; null cue
        // lists intentionally suppress configured cues.
        public string promptPriorityText;
        public string promptConditionText;
        public string promptDescriptionText;
        public List<string> promptCueTexts = new List<string>();

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

        // Cached pure-rule projections of the XML triggers. Built once on first use and reused: the
        // signal path is hot (Thing.SpawnSetup fires for every projectile/filth/item), so converting
        // the XML triggers into matcher DTOs on every signal would allocate a List plus one rule per
        // trigger for nothing. Defs are immutable after load, so caching here is safe.
        private List<EventWindowTriggerRule> startRulesCache;
        private List<EventWindowTriggerRule> endRulesCache;

        /// <summary>Cached pure trigger rules that can START this window.</summary>
        public List<EventWindowTriggerRule> StartRules()
        {
            return startRulesCache ?? (startRulesCache = RulesFrom(startSignals));
        }

        /// <summary>Cached pure trigger rules that can END this window.</summary>
        public List<EventWindowTriggerRule> EndRules()
        {
            return endRulesCache ?? (endRulesCache = RulesFrom(endSignals));
        }

        private static List<EventWindowTriggerRule> RulesFrom(List<DiaryEventWindowTriggerDef> triggers)
        {
            List<EventWindowTriggerRule> rules = new List<EventWindowTriggerRule>();
            if (triggers == null)
            {
                return rules;
            }

            for (int i = 0; i < triggers.Count; i++)
            {
                DiaryEventWindowTriggerDef trigger = triggers[i];
                if (trigger != null)
                {
                    rules.Add(trigger.ToRule());
                }
            }

            return rules;
        }

        /// <summary>
        /// Load-time validation for active windows. A persistent window must have some way to close, or
        /// its prompt context can survive forever after save/load.
        /// </summary>
        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors())
            {
                yield return error;
            }

            if (keepActive && timeoutTicks <= 0 && !HasAnyTrigger(endSignals))
            {
                yield return "keepActive=true requires timeoutTicks > 0 or at least one usable endSignals trigger; "
                    + "otherwise prompt context can stay active forever.";
            }
        }

        private static bool HasAnyTrigger(List<DiaryEventWindowTriggerDef> triggers)
        {
            if (triggers == null)
            {
                return false;
            }

            for (int i = 0; i < triggers.Count; i++)
            {
                DiaryEventWindowTriggerDef trigger = triggers[i];
                if (trigger == null)
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(trigger.source)
                    || !string.IsNullOrWhiteSpace(trigger.signal)
                    || HasAnyText(trigger.matchDefNames)
                    || HasAnyText(trigger.matchTokens))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasAnyText(List<string> values)
        {
            if (values == null)
            {
                return false;
            }

            for (int i = 0; i < values.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(values[i]))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
