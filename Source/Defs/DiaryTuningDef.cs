// Tuning knobs (the "magic numbers") for recording and context-building, pulled out of the
// code into a single Def so they can be retuned by editing XML (1.6/Defs/DiaryTuningDef.xml)
// and restarting — no recompile. Every field defaults to the value the code shipped with, so a
// missing or partial XML changes nothing. New to C#/RimWorld? See AGENTS.md ("Defs").
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
        // The same pawn+break is only recorded once per this window (~1 in-game day).
        public int mentalBreakDedupTicks = 2500;
        // The same TaleDef+pawn combination is only recorded once within this short window.
        public int taleDedupTicks = 2500;
        // Small-talk interactions for the same pawn pair are combined until this quiet window passes.
        public int smallTalkBatchWindowTicks = 2500;
        // Flush sooner when a pair talks this many times before the quiet window passes.
        public int smallTalkBatchMaxEvents = 6;

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
