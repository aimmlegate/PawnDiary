// Tuning knobs (the "magic numbers") for recording and context-building, pulled out of the
// code into a single Def so they can be retuned by editing XML (1.6/Defs/DiaryTuningDef.xml)
// and restarting — no recompile. Every field defaults to the value the code shipped with, so a
// missing or partial XML changes nothing. New to C#/RimWorld? See AGENTS.md ("Defs").
using System.Collections.Generic;
using PawnDiary.Capture;
using Verse;

namespace PawnDiary
{
    /// <summary>
    /// One tracked stage in a situational ThoughtDef progression. RimWorld exposes hunger, rest,
    /// outdoors, and chemical need moods as stages inside a single ThoughtDef, so XML gives each
    /// stage an explicit severity rank. Higher severity means "worse" for diary progression.
    /// </summary>
    public class ThoughtProgressionStage
    {
        public int stageIndex = -1;
        public int severity = 0;
    }

    /// <summary>
    /// XML tuning for one staged situational ThoughtDef, grouped by category so related def/stage
    /// changes dedupe together. New to C#/RimWorld? See AGENTS.md ("Defs").
    /// </summary>
    public class ThoughtProgressionRule
    {
        public string categoryKey;
        public string thoughtDefName;
        public List<ThoughtProgressionStage> stages;
    }

    /// <summary>
    /// XML tuning for one weather's diary-mention chance: the <c>weather</c> WeatherDef defName and
    /// the <c>chance</c> (0..1) that an outdoor entry notes it. Weathers absent from the list fall
    /// back to the favorability-keyed chances on <see cref="DiaryTuningDef"/>.
    /// </summary>
    public class WeatherMentionRule
    {
        public string weather;
        public float chance;
    }

    // One instance of this Def is expected, with defName "Diary_Tuning". Read it via
    // DiaryTuning.Current (which falls back to safe defaults if the Def is absent).
    public class DiaryTuningDef : Def
    {
        // ---- Deduplication windows (in game ticks; 60 ticks ≈ 1 second) ----
        // SocialFighting fires once per participant; collapse the mirrored call within this window.
        public int socialFightDedupTicks = 300;
        // The same pawn+break is only recorded once per this window (~1 in-game hour).
        public int mentalBreakDedupTicks = 2500;
        // The same TaleDef+pawn combination is only recorded once within this short window.
        public int taleDedupTicks = 2500;
        // The same mood-event GameConditionDef is only recorded once across the colony within this
        // window (the dedup is keyed by condition, not per colonist).
        public int moodEventDedupTicks = 2500;
        // The same pawn+thought combination is only recorded once within this window (~1 in-game hour).
        public int thoughtDedupTicks = 2500;
        // The same romance relation change for a pawn pair is only recorded once within this window.
        public int romanceDedupTicks = 2500;
        // The same raid incident (same incident/map/faction/points key) is only recorded once across
        // the colony within this window. Raids fire once per IncidentWorker.TryExecute, so this is
        // mostly defensive against a fluke double-fire or a mirrored multi-map transition.
        public int raidDedupTicks = 2500;
        // The same quest lifecycle signal (same quest id + signal) is only recorded once within this
        // window. Guards against a fluke double-call on Quest.Accept or Quest.End.
        public int questDedupTicks = 2500;
        // The same ritual outcome is only recorded once within this window. Guards against a fluke
        // double-call while still allowing separate rituals of the same type later.
        public int ritualDedupTicks = 2500;

        // ---- Surroundings scan ----
        public float nearbyRadius = 5f;       // cells searched around the pawn for notable things
        public int maxNearbyThings = 6;       // cap on nearby candidates considered before weighted pick
        public float coldBelowC = 0f;         // report "cold" at or below this temperature (°C)
        public float hotAboveC = 32f;         // report "hot" at or above this temperature (°C)

        // ---- Weather mentions ----
        // Chance (0..1) an outdoor entry mentions the current weather, per weather. Clear is 0 so it
        // is never noted; mild weather is low and dramatic weather high, to keep weather from
        // dominating diary openings. Weathers absent here use the favorability fallbacks below, so
        // DLC/modded weather still scales with severity. Keyed by WeatherDef.defName.
        public List<WeatherMentionRule> weatherMentionChances = new List<WeatherMentionRule>
        {
            new WeatherMentionRule { weather = "Clear", chance = 0f },
            new WeatherMentionRule { weather = "Overcast", chance = 0.2f },        // Odyssey
            new WeatherMentionRule { weather = "Fog", chance = 0.25f },
            new WeatherMentionRule { weather = "SnowGentle", chance = 0.3f },
            new WeatherMentionRule { weather = "Rain", chance = 0.35f },
            new WeatherMentionRule { weather = "Windy", chance = 0.45f },          // Odyssey
            new WeatherMentionRule { weather = "FoggyRain", chance = 0.45f },
            new WeatherMentionRule { weather = "BlindFog", chance = 0.6f },        // Odyssey
            new WeatherMentionRule { weather = "SnowHard", chance = 0.7f },
            new WeatherMentionRule { weather = "ToxRain", chance = 0.85f },        // Odyssey
            new WeatherMentionRule { weather = "TorrentialRain", chance = 0.85f }, // Odyssey
            new WeatherMentionRule { weather = "DryThunderstorm", chance = 0.9f },
            new WeatherMentionRule { weather = "RainyThunderstorm", chance = 0.9f },
            new WeatherMentionRule { weather = "Sandstorm", chance = 0.9f },       // Odyssey
            new WeatherMentionRule { weather = "Blizzard", chance = 0.95f },       // Odyssey
            new WeatherMentionRule { weather = "BloodRain", chance = 1f },         // Anomaly
            new WeatherMentionRule { weather = "GrayPall", chance = 1f },          // Anomaly
            new WeatherMentionRule { weather = "DeathPall", chance = 1f },         // Anomaly
        };

        // Fallback mention chances for weathers not in weatherMentionChances, keyed by the
        // WeatherDef's favorability. Good/OuterSpace (and anything unmatched) use weatherChanceDefault.
        public float weatherChanceVeryBad = 0.9f;
        public float weatherChanceBad = 0.5f;
        public float weatherChanceNeutral = 0.25f;
        public float weatherChanceDefault = 0f;

        // ---- Health ----
        public float painVisibleAbove = 0.03f;     // report pain only above this fraction
        public float bleedVisibleAbove = 0.01f;    // report bleeding only above this rate
        public float lowCapacityThreshold = 0.80f; // report a capacity only when below this level

        // ---- Misc ----
        public int diaryLineMaxChars = 160;   // truncate the "last wrote" continuity line to this
        // Minimum biological age for first-person diary ownership/generation. Pre-teen colonists can
        // still appear as context in someone else's entry, but they do not write their own pages.
        public int minimumFirstPersonAgeYears = 13;

        // ---- Beauty buckets (the "notable" gate uses beautyPleasant as the ± threshold) ----
        public float beautyBeautiful = 2f;
        public float beautyPleasant = 0.3f;
        public float beautyUgly = -2f;

        // ---- Mood buckets (percent) ----
        public int moodHappy = 75;
        public int moodStable = 50;
        public int moodStressed = 30;

        // ---- Pain buckets (fraction) ----
        public float painSevere = 0.4f;
        public float painModerate = 0.18f;

        // ---- Opinion buckets (opinion points) ----
        public int opinionDevoted = 60;
        public int opinionFriendly = 25;
        public int opinionNeutralAbove = -10;   // opinion > this => "neutral" (else worse)
        public int opinionStrainedAbove = -40;  // opinion > this => "strained" (else "hostile")

        // ---- Thought recording thresholds ----
        // Minimum absolute mood offset for a general thought to be recorded.
        public float thoughtMinMoodOffset = 5f;
        // Minimum absolute mood offset for an eating-related thought to be recorded.
        public float thoughtEatingMinMoodOffset = 15f;

        // Substring tokens: a ThoughtDef defName containing any token (case-insensitive) is always
        // ignored — never recorded as a diary entry. Used for room stat thoughts, corpse observations, etc.
        public List<string> thoughtIgnoreTokens;

        // Substring tokens: a ThoughtDef defName containing any token (case-insensitive) bypasses
        // the magnitude threshold — always recorded regardless of mood offset (if it has expiration).
        // Used for death thoughts, banishment, abandonment, etc.
        public List<string> thoughtBypassThresholdTokens;

        // Substring tokens: a ThoughtDef defName containing any token (case-insensitive) is classified
        // as an eating thought and uses thoughtEatingMinMoodOffset instead of thoughtMinMoodOffset.
        public List<string> thoughtEatingTokens;

        // Substring tokens: a ThoughtDef defName containing any token (case-insensitive) becomes
        // ambient day-note material instead of an immediate solo entry, after normal thresholds/dedup.
        public List<string> thoughtAmbientTokens;
        // Ambient temporary thoughts collect until the day changes or this quiet window passes.
        public int thoughtAmbientWindowTicks = 60000;
        // Drop ambient thought notes unless at least this many matching thoughts accumulated.
        public int thoughtAmbientMinEventsToWrite = 2;
        // Keep at most this many thought evidence lines in the prompt.
        public int thoughtAmbientMaxSampleLines = 5;

        // ---- Thought progression scanner ----
        // Situational need thoughts (food/rest/outdoors/chemical desire) are not gained through
        // MemoryThoughtHandler.TryGainMemory, so a lightweight scan watches their active stages.
        public int thoughtProgressionScanIntervalTicks = 250;
        public int thoughtProgressionDedupTicks = 2500;
        public List<ThoughtProgressionRule> thoughtProgressionRules = new List<ThoughtProgressionRule>
        {
            new ThoughtProgressionRule
            {
                categoryKey = "food",
                thoughtDefName = "NeedFood",
                stages = new List<ThoughtProgressionStage>
                {
                    new ThoughtProgressionStage { stageIndex = 2, severity = 1 },
                    new ThoughtProgressionStage { stageIndex = 3, severity = 2 },
                    new ThoughtProgressionStage { stageIndex = 4, severity = 3 },
                    new ThoughtProgressionStage { stageIndex = 5, severity = 4 },
                    new ThoughtProgressionStage { stageIndex = 6, severity = 5 }
                }
            },
            new ThoughtProgressionRule
            {
                categoryKey = "rest",
                thoughtDefName = "NeedRest",
                stages = new List<ThoughtProgressionStage>
                {
                    new ThoughtProgressionStage { stageIndex = 1, severity = 1 },
                    new ThoughtProgressionStage { stageIndex = 2, severity = 2 }
                }
            },
            new ThoughtProgressionRule
            {
                categoryKey = "outdoors",
                thoughtDefName = "NeedOutdoors",
                stages = new List<ThoughtProgressionStage>
                {
                    new ThoughtProgressionStage { stageIndex = 1, severity = 1 },
                    new ThoughtProgressionStage { stageIndex = 0, severity = 2 }
                }
            },
            new ThoughtProgressionRule
            {
                categoryKey = "chemical",
                thoughtDefName = "DrugDesireInterest",
                stages = new List<ThoughtProgressionStage>
                {
                    new ThoughtProgressionStage { stageIndex = 1, severity = 1 },
                    new ThoughtProgressionStage { stageIndex = 2, severity = 2 }
                }
            },
            new ThoughtProgressionRule
            {
                categoryKey = "chemical",
                thoughtDefName = "DrugDesireFascination",
                stages = new List<ThoughtProgressionStage>
                {
                    new ThoughtProgressionStage { stageIndex = 1, severity = 1 },
                    new ThoughtProgressionStage { stageIndex = 2, severity = 2 }
                }
            }
        };

        // ---- Hediff progression scanner ----
        // AddHediff catches new health conditions. This scanner watches active matched hediffs for
        // XML-configured severity-step increases, so modded conditions can worsen into diary signals
        // without a per-mod Harmony patch.
        public int hediffProgressionScanIntervalTicks = 2500;

        // ---- Work recording ----
        // Periodic scanner interval for colonists' current work jobs. Work has no clean one-shot
        // RimWorld event for "this was a diary-worthy work moment", so the scanner samples rarely
        // and then applies the chance/cooldown gates below.
        public int workScanIntervalTicks = 2500;
        // Base probability per scan that a currently working pawn writes about that work type.
        public float workBaseChance = 0.08f;
        // After a pawn gets a work entry for one WorkTypeDef, suppress that same work type for this
        // many ticks (~3 in-game days by default).
        public int workSameTypeCooldownTicks = 180000;
        // If the pawn had any other work entry inside the same cooldown window, halve the roll so
        // different work can still surface without filling the diary.
        public float workRecentDifferentTypeMultiplier = 0.5f;
        // Relative chance nudges. The final roll is still clamped to 0..1.
        public float workPassionChanceMultiplier = 1.4f;
        public float workNegativeChanceMultiplier = 1.2f;
        public float workDarkStudyChanceMultiplier = 1.5f;
        // A non-passion work type whose best relevant skill is below this level reads as uncertain
        // or frustrating instead of confident.
        public int workLowSkillThreshold = 4;

        // ---- Day reflection (end-of-day summary) ----
        // Master toggle. When false, the old per-source ambient notes are emitted as before.
        public bool daySummaryEnabled = true;
        // Highest number of highlights woven into one reflection (the weighted selection cap).
        public int daySummaryMaxHighlights = 3;
        // Which candidate kinds are strong enough to create a reflection. Valid tokens are event,
        // opinion, hediff, and filler. Filler is excluded by default so small talk can add color but
        // cannot create an otherwise-empty daily summary.
        public List<string> daySummaryImportantSignalKinds = new List<string>
        {
            DayReflectionEventData.SignalKindEvent,
            DayReflectionEventData.SignalKindOpinion,
            DayReflectionEventData.SignalKindHediff,
        };
        // A colonist→colonist opinion swing of at least this many points (vs the day-start snapshot)
        // becomes a social-dynamic signal.
        public int daySummaryOpinionDeltaThreshold = 15;
        // Relative selection weights (higher = more likely to survive selection).
        public float daySummaryWeightCriticalEvent = 1f;   // combat / mental-state day events
        public float daySummaryWeightMajorEvent = 0.7f;    // other "important" day events
        public float daySummaryWeightHediff = 0.8f;        // a hediff health signal
        public float daySummaryWeightOpinionShift = 0.6f;  // base; scaled up by swing magnitude
        public float daySummaryWeightFiller = 0.15f;       // background small talk / passing feelings
    }

    // Accessor for the single DiaryTuningDef. Caches the lookup and falls back to a default
    // instance (with the field initializers above) if no Def is loaded, so the code never
    // NullReferences and behaves identically to the pre-Def version when the XML is absent.
    public static class DiaryTuning
    {
        private static DiaryTuningDef cached;
        private static readonly DiaryTuningDef Fallback = new DiaryTuningDef();

        public static DiaryTuningDef Current
        {
            get
            {
                if (cached == null)
                {
                    cached = DefDatabase<DiaryTuningDef>.GetNamedSilentFail("Diary_Tuning");
                }

                return cached ?? Fallback;
            }
        }
    }
}
