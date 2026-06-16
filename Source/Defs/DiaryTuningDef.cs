// Tuning knobs (the "magic numbers") for recording and context-building, pulled out of the
// code into a single Def so they can be retuned by editing XML (1.6/Defs/DiaryTuningDef.xml)
// and restarting — no recompile. Every field defaults to the value the code shipped with, so a
// missing or partial XML changes nothing. New to C#/RimWorld? See AGENTS.md ("Defs").
using System.Collections.Generic;
using Verse;

namespace PawnDiary
{
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

        // ---- Surroundings scan ----
        public float nearbyRadius = 5f;       // cells searched around the pawn for notable things
        public int maxNearbyThings = 6;       // cap on how many nearby things are listed
        public float coldBelowC = 0f;         // report "cold" at or below this temperature (°C)
        public float hotAboveC = 32f;         // report "hot" at or above this temperature (°C)

        // ---- Health ----
        public float painVisibleAbove = 0.03f;     // report pain only above this fraction
        public float bleedVisibleAbove = 0.01f;    // report bleeding only above this rate
        public float lowCapacityThreshold = 0.80f; // report a capacity only when below this level

        // ---- Misc ----
        public int diaryLineMaxChars = 160;   // truncate the "last wrote" continuity line to this

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
        // A colonist→colonist opinion swing of at least this many points (vs the day-start snapshot)
        // becomes a social-dynamic signal.
        public int daySummaryOpinionDeltaThreshold = 15;
        // A newly-appeared hediff counts as "major" at/above this severity (or if chronic / an
        // addiction / makes a sick thought — any one qualifies regardless of severity).
        public float daySummaryHediffMinSeverity = 0.3f;
        // Relative selection weights (higher = more likely to survive selection).
        public float daySummaryWeightCriticalEvent = 1f;   // combat / mental-state day events
        public float daySummaryWeightMajorEvent = 0.7f;    // other "important" day events
        public float daySummaryWeightHediff = 0.8f;        // a major new hediff
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
