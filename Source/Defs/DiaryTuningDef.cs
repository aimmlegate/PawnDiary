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
        // Ordinary raids usually spend time approaching before combat starts. The diary records the
        // event immediately but waits this many ticks before sending the LLM request, so generated
        // text can lean into anticipation instead of assuming the fight already happened. Drop-pod
        // raids and infestations bypass this delay.
        public int raidGenerationDelayTicks = 2500;
        // The same quest lifecycle signal (same quest id + signal) is only recorded once within this
        // window. Guards against a fluke double-call on Quest.Accept or Quest.End.
        public int questDedupTicks = 2500;
        // The same ritual outcome is only recorded once within this window. Guards against a fluke
        // double-call while still allowing separate rituals of the same type later.
        public int ritualDedupTicks = 2500;
        // The same ability activation is only recorded once within this window. The key also includes
        // the current tick, so this is mostly a defensive guard against paired Activate overloads.
        public int abilityDedupTicks = 300;
        // The same integration-API event (same eventKey + pawn/pair) is only recorded once within
        // this window (~1 in-game hour), unless the submitting adapter overrides dedup per request.
        public int externalEventDedupTicks = 2500;
        // Short safety net for sources without a detailed source-specific key, plus cross-source
        // shapes that intentionally share a type key (currently neutral death-description pages).
        // This is only meant to catch duplicate hook emissions in the same moment, not long cooldowns.
        public int genericEventTypeDedupTicks = 60;

        // ---- Ability-use sampling ----
        // Successful ability activations are sampled by cooldown. A no/short-cooldown ability uses
        // abilityUseMinChance; longer cooldowns approach abilityUseMaxChance. The reference value is
        // the cooldown where the curve reaches roughly halfway between min and max.
        public float abilityUseMinChance = 0.03f;
        public float abilityUseMaxChance = 0.75f;
        public int abilityUseReferenceCooldownTicks = 60000;

        // ---- Ritual quality wording ----
        // Ordered map from RimWorld's ritual progress/power value to a plain saved context label.
        // The prompt sees this label but is instructed to treat it as weight/aftermath, not to quote it.
        public List<RitualQualityBand> ritualQualityBands = RitualEventData.DefaultQualityBands();

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

        // ---- Prompt enchantments ----
        // These bands drive the optional one-line "important context" prompt cue. The Defs still own
        // which hediffs/capacities are eligible; these values only tune severity bucket thresholds and
        // how many cue fragments survive formatting.
        public float promptEnchantmentMinorHediffSeverity = PromptEnchantmentTuning.DefaultMinorHediffSeverity;
        public float promptEnchantmentModerateHediffSeverity = PromptEnchantmentTuning.DefaultModerateHediffSeverity;
        public float promptEnchantmentMajorHediffSeverity = PromptEnchantmentTuning.DefaultMajorHediffSeverity;
        public float promptEnchantmentCriticalHediffSeverity = PromptEnchantmentTuning.DefaultCriticalHediffSeverity;
        public float promptEnchantmentCloudedConsciousnessBelow = PromptEnchantmentTuning.DefaultCloudedConsciousnessBelow;
        public float promptEnchantmentFadingConsciousnessBelow = PromptEnchantmentTuning.DefaultFadingConsciousnessBelow;
        public float promptEnchantmentBarelyConsciousBelow = PromptEnchantmentTuning.DefaultBarelyConsciousBelow;
        public int promptEnchantmentMaxImpactCues = PromptEnchantmentTuning.DefaultMaxImpactCues;

        // ---- Consciousness: first-person generation gate ----
        // Pawns below this Consciousness level do not write first-person entries. Events still
        // record and neutral death/arrival descriptions still generate; only non-neutral LLM work
        // waits until the pawn is conscious enough again. Kept separate from the display staggering
        // thresholds below because this gate is about prompt authorship, not typography.
        public float minimumConsciousnessForFirstPersonGeneration = 0.11f;

        // ---- Display staggering: low-consciousness handwriting distortion ----
        // The 0..4 "staggered handwriting" intensity saved on each DiaryEvent POV. A pawn whose
        // Consciousness level is below a band's threshold gets at least that intensity. Higher
        // intensity means more distorted text. Read at capture time by PawnFactCapture and applied
        // (for display) through the XML decoration rules. Values are upper bounds per intensity
        // step; the first band the level falls into wins (checked 4 -> 1).
        public float staggeredConsciousnessIntensity4Below = 0.14f;
        public float staggeredConsciousnessIntensity3Below = 0.20f;
        public float staggeredConsciousnessIntensity2Below = 0.35f;
        public float staggeredConsciousnessIntensity1Below = 0.55f;

        // ---- Display staggering: intoxication severity distortion ----
        // Same 0..4 intensity scale, but driven by an intoxicating hediff's severity. A hediff is
        // treated as intoxicating only when it matches the XML decoration rules (see
        // Diary_TextDecorations), so the classification list is data-owned and DLC/mod extensible.
        // Values are lower bounds per intensity step; the first band the severity reaches wins
        // (checked 4 -> 1).
        public float intoxicationSeverityIntensity4At = 1.05f;
        public float intoxicationSeverityIntensity3At = 0.80f;
        public float intoxicationSeverityIntensity2At = 0.55f;
        public float intoxicationSeverityIntensity1At = 0.30f;

        // ---- Mood-impact condition families ----
        // GameCondition defNames that are always positive (or always negative) for affected
        // colonists, used as the name-based fallback in DetermineMoodImpact when a condition has no
        // measurable mood offset. Matched case-insensitively by exact defName. These are plain
        // strings, never def references, so a DLC-only entry like GrayPall (Anomaly) sits inert
        // without its DLC — see AGENTS.md ("DLC-safety").
        public List<string> positiveMoodConditionDefNames = new List<string>
        {
            "Aurora",
            "Party",
            "PsychicSoothe",
            "PsychicEmanation"
        };
        public List<string> negativeMoodConditionDefNames = new List<string>
        {
            "Eclipse",
            "ToxicFallout",
            "VolcanicWinter",
            "Flashstorm",
            "GrayPall"
        };

        // ---- Misc ----
        public int diaryLineMaxChars = 160;   // truncate the "last wrote" continuity line to this
        // Previous-entry ending context shown to the next first-person prompt so entries can continue
        // from the prior page without sending a long history. XML-tunable; code clamps defensively.
        public int previousEntryEndingSentenceCount = 2;
        public int previousEntryEndingMaxChars = 280;
        // Newest diary events (colony-wide) treated as "hot" for background maintenance scans and
        // prompt-history context. Older entries remain saved and visible, but are archive history that
        // is not retried or backfilled by catch-up scanners. This is a global count across all pawns;
        // the per-pawn hot and archived history caps are mod settings.
        public int activeScanEventWindow = 1000;
        // Archived pending entries fall back to a prompt-fact card instead of an endless "writing..."
        // indicator. These tune the generated display-only fallback.
        public int archivedFallbackTitleWords = 6;
        public int archivedFallbackTextMaxChars = 240;
        // Public integration API context snapshots expose recent completed prose as first-sentence
        // summaries. These caps bound the read so a chat adapter cannot accidentally pull a long
        // diary history into another prompt.
        public int integrationContextMaxEntries = 8;
        public int integrationContextSummaryMaxChars = 220;
        // GetEntryStats walks the archive newest-first counting matching rows. Without a cap a
        // long-lived colonist's full archive would be scanned per stats call. This bounds the scan so
        // the read stays main-thread cheap; counts are approximate beyond the cap. Sibling reads
        // (titles, prose) are already bounded by their returned-list limit.
        public int integrationStatsMaxArchiveScan = 500;
        // Ordinary external-event submissions may add a sanitized prompt fragment and compact
        // prompt-enchantment candidates. These caps keep adapter-authored prompt context bounded.
        public int integrationPromptFragmentMaxChars = 1200;
        public int integrationPromptEnchantmentMaxCandidates = 6;
        public int integrationPromptEnchantmentCandidateMaxChars = 160;
        public float integrationPromptEnchantmentCandidateWeight = 1.5f;
        // Direct-text integration writes caller-authored prose straight into the save. These caps keep
        // a noisy adapter from bloating saves or card headers while remaining XML-tunable.
        public int integrationDirectTextMaxChars = 4000;
        public int integrationDirectTitleMaxChars = 120;
        // Rolling-window guardrails for public integration API calls that can enqueue LLM work.
        // Counts are per accepted API request; token caps use a conservative maxTokens estimate.
        public bool integrationPromptBudgetEnabled = true;
        public int integrationPromptBudgetWindowTicks = 2500;
        public int integrationPromptBudgetMaxRequestsPerSource = 10;
        public int integrationPromptBudgetMaxRequestsGlobal = 30;
        public int integrationPromptBudgetMaxTokensPerSource = 20000;
        public int integrationPromptBudgetMaxTokensGlobal = 60000;
        // Diary UI long-history indexing is sliced across frames so selecting a pawn never scans
        // thousands of entries in one draw. These cap per-frame work for the tab and command badges.
        public int uiHistoryScanMaxEventsPerFrame = 60;
        public float uiHistoryScanFrameBudgetSeconds = 0.00075f;
        // Minimum biological age for first-person diary ownership/generation. Below this, colonists can
        // still appear as context in someone else's entry, but they do not write their own pages. Lowered
        // to 7 so children can keep a diary in the naive child voice/psychotype catalogs; both layers
        // re-roll onto the adult catalogs when the pawn crosses psychotypeCrystallizationAgeYears.
        public int minimumFirstPersonAgeYears = 7;

        // Biological age at which a pawn's voice "crystallizes": both the writing style and the
        // psychotype re-roll from the child catalogs onto the adult catalogs. 13 is the final vanilla
        // growth moment (a pawn's passion set is complete by then). Pinned (player-chosen) layers are
        // never auto-re-rolled. See DiaryGameComponent.EnsureVoiceStage.
        public int psychotypeCrystallizationAgeYears = 13;

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

        // ---- Body-part event policy ----
        // Plain defName strings only: DLC/modded entries that are absent from a player's install
        // simply never appear. These tune body-part diary events without hard-referencing content.
        public List<string> bodyPartTierOverrideAnomalous = new List<string>
        {
            "AdrenalHeart",
            "CorrosiveHeart",
            "MetalbloodHeart",
            "RevenantVertebrae"
        };
        public List<string> bodyPartTierOverrideCrude = new List<string>();
        public List<string> bodyPartTierOverrideProsthetic = new List<string>();
        public List<string> bodyPartTierOverrideBionic = new List<string>();
        public List<string> bodyPartTierOverrideArchotech = new List<string>();
        public List<string> bodyPartCravesTraitDefNames = new List<string> { "Transhumanist" };
        public List<string> bodyPartDespisesTraitDefNames = new List<string> { "BodyPurist" };
        public List<string> bodyPartApprovePreceptDefNames = new List<string> { "BodyMod_Approved" };
        public List<string> bodyPartDespisePreceptDefNames = new List<string>
        {
            "BodyMod_Disapproved",
            "BodyMod_Abhorrent"
        };
        public List<string> bodyPartInhumanizedHediffDefNames = new List<string> { "Inhumanized" };
        public float bodyPartCrudeEfficiencyBelow = BodyPartEventPolicy.DefaultCrudeEfficiencyBelow;
        public float bodyPartProstheticEfficiencyMax = BodyPartEventPolicy.DefaultProstheticEfficiencyMax;
        public float bodyPartBionicEfficiencyMax = BodyPartEventPolicy.DefaultBionicEfficiencyMax;

        // ---- Pawn progression scanner ----
        // Watches slow-changing pawn state that is not reliably covered by one-shot hooks: passion
        // skill milestones, psylink levels, xenotype changes, and royal-title changes.
        public int progressionScanIntervalTicks = 2500;
        public List<int> progressionSkillMilestones = new List<int> { 8, 12, 16, 20 };
        // Plain string matchers, not Def references, so Royalty-less games simply never see a match.
        public List<string> psylinkHediffDefNames = new List<string> { "PsychicAmplifier" };

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
        // Rare long reflection near the end of each quadrum. The timing window spreads pawns across
        // several days, and maxPromptEvents is the prompt evidence cap so long histories stay bounded.
        public bool quadrumReflectionEnabled = true;
        public int quadrumReflectionTimingWindowDays = 3;
        public int quadrumReflectionMinImportantEntries = 6;
        public int quadrumReflectionMaxPromptEvents = 8;
        public int quadrumReflectionMaxTokens = 350;

        // ---- Arc reflections ----
        public bool arcReflectionEnabled = true;
        public int arcReflectionMaxEntriesPerYear = 1;
        public bool arcReflectionAllowSecondMajorEntry = true;
        public int arcReflectionSecondEntryMinGapDays = 30;
        // 0..100 threshold used by major progression triggers; default 90 keeps only the biggest moments.
        public int arcReflectionMajorSeverityThreshold = 90;
        public int arcReflectionForceAfterYearDay = 45;
        // Retry delay after a forced yearly arc finds too few memories; prevents 250-tick rest rescans.
        public int arcReflectionMemoryShortfallRetryTicks = 60000;
        public int arcReflectionMinMemoriesPreferred = 4;
        public int arcReflectionMinMemoriesForced = 3;
        public int arcReflectionMaxMemories = 8;
        public int arcReflectionRecentlyUsedMemoryCap = 16;
        public int arcReflectionMemorySnippetMaxChars = 220;
        public int arcReflectionMaxTokens = 420;
        // Plain defName strings/tokens, not Def references, so missing DLC content simply never matches.
        public List<string> arcReflectionMajorXenotypeDefNames = new List<string> { "Sanguophage" };
        public List<string> arcReflectionHighStakesDefNameTokens = new List<string>
        {
            "Void",
            "HeartAttack",
            "AncientDanger",
            "PrisonBreak",
        };

        // ---- Humor cues (hidden, always-on) ----
        // Base probability (0..1) that an eligible first-person entry gets one structural humor
        // cue appended to its prompt. There is no settings field or UI for this; the single knob
        // lives here so it can be retuned in XML. Flavor (Light vs Gallows) is chosen separately by
        // event stakes, so this only controls how often any humor appears at all.
        public float humorChance = 0.20f;
        // Flat multiplier on humorChance for a writer with an upbeat temperament (Optimist, Sanguine,
        // or Anomaly's Joyous trait) or a Social skill passion (minor or burning). Not cumulative:
        // matching several of those qualifiers still applies this multiplier once, never stacked. See
        // HumorCues.HumorChanceMultiplierFor.
        public float humorElevatedChanceMultiplier = 2f;
        // Flat multiplier on humorChance for a writer with a dour/anxious/unfeeling temperament
        // (Pessimist, Depressive, Nervous, Neurotic, Very neurotic, Psychopath, or Anomaly's
        // Disturbing trait). Below 1 to make humor rarer. Non-cumulative and mutually exclusive with
        // the elevated multiplier: a writer who qualifies for both offsets back to the base rate.
        public float humorReducedChanceMultiplier = 0.5f;
    }

    // Accessor for the single DiaryTuningDef. Caches the lookup and falls back to a default
    // instance (with the field initializers above) if no Def is loaded, so the code never
    // NullReferences and behaves identically to the pre-Def version when the XML is absent.
    internal static class DiaryTuning
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

        // Fallback base rate used only if the XML def is missing entirely. When the def exists but a
        // modder leaves humorChance blank, the field initializer (0.20f) already applies. This clamps
        // any out-of-range authored value to 0..1 so a typo can never make humor fire >100% or crash.
        private const float DefaultHumorChance = 0.20f;

        /// <summary>
        /// XML-tuned base probability (0..1) that an eligible first-person entry gets a humor cue.
        /// Reads <see cref="DiaryTuningDef.humorChance"/> from the loaded def, clamped to 0..1, with
        /// a hardcoded fallback when the tuning def is absent. See <c>HumorCues</c>.
        /// </summary>
        public static float HumorChance
        {
            get
            {
                float value = Current.humorChance;
                if (value < 0f || value > 1f || float.IsNaN(value))
                {
                    return DefaultHumorChance;
                }

                return value;
            }
        }

        // Fallbacks used only if the XML def is missing entirely or the authored value is negative/NaN;
        // mirror the HumorChance guard above.
        private const float DefaultHumorElevatedChanceMultiplier = 2f;
        private const float DefaultHumorReducedChanceMultiplier = 0.5f;

        /// <summary>
        /// XML-tuned flat multiplier applied to <see cref="HumorChance"/> for a writer who qualifies
        /// for the elevated humor chance (see <c>HumorCues.HumorChanceMultiplierFor</c>). Reads
        /// <see cref="DiaryTuningDef.humorElevatedChanceMultiplier"/>, with a hardcoded fallback when
        /// the tuning def is absent or the authored value is negative/NaN.
        /// </summary>
        public static float HumorElevatedChanceMultiplier
        {
            get
            {
                float value = Current.humorElevatedChanceMultiplier;
                if (value < 0f || float.IsNaN(value))
                {
                    return DefaultHumorElevatedChanceMultiplier;
                }

                return value;
            }
        }

        /// <summary>
        /// XML-tuned flat multiplier applied to <see cref="HumorChance"/> for a writer with a dour/
        /// anxious/unfeeling temperament (see <c>HumorCues.HumorChanceMultiplierFor</c>). Reads
        /// <see cref="DiaryTuningDef.humorReducedChanceMultiplier"/>, with a hardcoded fallback when
        /// the tuning def is absent or the authored value is negative/NaN.
        /// </summary>
        public static float HumorReducedChanceMultiplier
        {
            get
            {
                float value = Current.humorReducedChanceMultiplier;
                if (value < 0f || float.IsNaN(value))
                {
                    return DefaultHumorReducedChanceMultiplier;
                }

                return value;
            }
        }

        /// <summary>
        /// XML-tuned count of newest diary events that remain active for background maintenance scans.
        /// Older entries are archive history: still rendered, but not retried or title-backfilled.
        /// </summary>
        public static int ActiveScanEventWindow
        {
            get
            {
                int value = Current.activeScanEventWindow;
                return value > 0 ? value : Fallback.activeScanEventWindow;
            }
        }

        /// <summary>
        /// Number of words used for the date-line fallback title on archived entries that never
        /// generated. This title is display-only; it is not saved back to the event.
        /// </summary>
        public static int ArchivedFallbackTitleWords
        {
            get { return PositiveOrDefault(Current.archivedFallbackTitleWords, Fallback.archivedFallbackTitleWords); }
        }

        /// <summary>
        /// Maximum length of the prompt-fact fallback body for archived entries that never generated.
        /// </summary>
        public static int ArchivedFallbackTextMaxChars
        {
            get { return PositiveOrDefault(Current.archivedFallbackTextMaxChars, Fallback.archivedFallbackTextMaxChars); }
        }

        /// <summary>
        /// Maximum recent diary prose summaries returned by the public integration context snapshot.
        /// </summary>
        public static int IntegrationContextMaxEntries
        {
            get { return PositiveOrDefault(Current.integrationContextMaxEntries, Fallback.integrationContextMaxEntries); }
        }

        /// <summary>
        /// Maximum archive rows scanned by one GetEntryStats call (newest-first). Counts become
        /// approximate beyond this cap; it exists so a long-lived colonist's full archive is never
        /// walked on a single main-thread stats read.
        /// </summary>
        public static int IntegrationStatsMaxArchiveScan
        {
            get { return PositiveOrDefault(Current.integrationStatsMaxArchiveScan, Fallback.integrationStatsMaxArchiveScan); }
        }

        /// <summary>
        /// Maximum characters in each public integration prose summary.
        /// </summary>
        public static int IntegrationContextSummaryMaxChars
        {
            get { return PositiveOrDefault(Current.integrationContextSummaryMaxChars, Fallback.integrationContextSummaryMaxChars); }
        }

        /// <summary>
        /// Maximum characters accepted for an external event's protected prompt fragment.
        /// </summary>
        public static int IntegrationPromptFragmentMaxChars
        {
            get { return PositiveOrDefault(Current.integrationPromptFragmentMaxChars, Fallback.integrationPromptFragmentMaxChars); }
        }

        /// <summary>
        /// Maximum prompt-enchantment candidate lines accepted from one external event.
        /// </summary>
        public static int IntegrationPromptEnchantmentMaxCandidates
        {
            get { return PositiveOrDefault(Current.integrationPromptEnchantmentMaxCandidates, Fallback.integrationPromptEnchantmentMaxCandidates); }
        }

        /// <summary>
        /// Maximum characters in each external prompt-enchantment candidate line.
        /// </summary>
        public static int IntegrationPromptEnchantmentCandidateMaxChars
        {
            get { return PositiveOrDefault(Current.integrationPromptEnchantmentCandidateMaxChars, Fallback.integrationPromptEnchantmentCandidateMaxChars); }
        }

        /// <summary>
        /// XML-tuned planner weight assigned to each external prompt-enchantment candidate.
        /// </summary>
        public static float IntegrationPromptEnchantmentCandidateWeight
        {
            get { return NonNegativeOrDefault(Current.integrationPromptEnchantmentCandidateWeight, Fallback.integrationPromptEnchantmentCandidateWeight); }
        }

        /// <summary>
        /// Maximum saved body length accepted from external direct-text injection.
        /// </summary>
        public static int IntegrationDirectTextMaxChars
        {
            get { return PositiveOrDefault(Current.integrationDirectTextMaxChars, Fallback.integrationDirectTextMaxChars); }
        }

        /// <summary>
        /// Maximum saved title length accepted from external direct-text injection.
        /// </summary>
        public static int IntegrationDirectTitleMaxChars
        {
            get { return PositiveOrDefault(Current.integrationDirectTitleMaxChars, Fallback.integrationDirectTitleMaxChars); }
        }

        /// <summary>
        /// XML-tuned rolling-window budget for external API requests that can enqueue LLM work.
        /// Internal because <see cref="ExternalApiBudgetTuning"/> is an implementation-only DTO;
        /// the public integration API never exposes the budget knobs.
        /// </summary>
        internal static ExternalApiBudgetTuning IntegrationPromptBudgetTuning
        {
            get
            {
                DiaryTuningDef tuning = Current;
                DiaryTuningDef fallback = Fallback;
                return new ExternalApiBudgetTuning
                {
                    enabled = tuning.integrationPromptBudgetEnabled,
                    windowTicks = NonNegativeOrDefault(
                        tuning.integrationPromptBudgetWindowTicks,
                        fallback.integrationPromptBudgetWindowTicks),
                    maxRequestsPerSource = NonNegativeOrDefault(
                        tuning.integrationPromptBudgetMaxRequestsPerSource,
                        fallback.integrationPromptBudgetMaxRequestsPerSource),
                    maxRequestsGlobal = NonNegativeOrDefault(
                        tuning.integrationPromptBudgetMaxRequestsGlobal,
                        fallback.integrationPromptBudgetMaxRequestsGlobal),
                    maxTokensPerSource = NonNegativeOrDefault(
                        tuning.integrationPromptBudgetMaxTokensPerSource,
                        fallback.integrationPromptBudgetMaxTokensPerSource),
                    maxTokensGlobal = NonNegativeOrDefault(
                        tuning.integrationPromptBudgetMaxTokensGlobal,
                        fallback.integrationPromptBudgetMaxTokensGlobal)
                };
            }
        }

        /// <summary>
        /// Maximum saved events the Diary UI may index in one frame while loading a long pawn history.
        /// </summary>
        public static int UiHistoryScanMaxEventsPerFrame
        {
            get { return PositiveOrDefault(Current.uiHistoryScanMaxEventsPerFrame, Fallback.uiHistoryScanMaxEventsPerFrame); }
        }

        /// <summary>How many final days of a quadrum can host long reflections, used to stagger pawns.</summary>
        public static int QuadrumReflectionTimingWindowDays
        {
            get { return PositiveOrDefault(Current.quadrumReflectionTimingWindowDays, Fallback.quadrumReflectionTimingWindowDays); }
        }

        /// <summary>Minimum important entries in the quadrum before the long reflection may write.</summary>
        public static int QuadrumReflectionMinImportantEntries
        {
            get { return PositiveOrDefault(Current.quadrumReflectionMinImportantEntries, Fallback.quadrumReflectionMinImportantEntries); }
        }

        /// <summary>Per-request output cap for the long quadrum reflection prompt.</summary>
        public static int QuadrumReflectionMaxTokens
        {
            get { return PositiveOrDefault(Current.quadrumReflectionMaxTokens, Fallback.quadrumReflectionMaxTokens); }
        }

        /// <summary>
        /// Approximate wall-clock budget per frame for sliced Diary UI history indexing.
        /// </summary>
        public static float UiHistoryScanFrameBudgetSeconds
        {
            get { return NonNegativeOrDefault(Current.uiHistoryScanFrameBudgetSeconds, Fallback.uiHistoryScanFrameBudgetSeconds); }
        }

        /// <summary>
        /// XML-tuned prompt-enchantment thresholds and cue cap, normalized into a plain DTO for the
        /// collector/planner split. Negative or NaN values fall back to the shipped defaults; zero is
        /// allowed for the cue cap so XML can intentionally suppress cues.
        /// </summary>
        public static PromptEnchantmentTuning PromptEnchantmentTuning
        {
            get
            {
                DiaryTuningDef tuning = Current;
                return new PawnDiary.PromptEnchantmentTuning
                {
                    minorHediffSeverity = NonNegativeOrDefault(
                        tuning.promptEnchantmentMinorHediffSeverity,
                        PawnDiary.PromptEnchantmentTuning.DefaultMinorHediffSeverity),
                    moderateHediffSeverity = NonNegativeOrDefault(
                        tuning.promptEnchantmentModerateHediffSeverity,
                        PawnDiary.PromptEnchantmentTuning.DefaultModerateHediffSeverity),
                    majorHediffSeverity = NonNegativeOrDefault(
                        tuning.promptEnchantmentMajorHediffSeverity,
                        PawnDiary.PromptEnchantmentTuning.DefaultMajorHediffSeverity),
                    criticalHediffSeverity = NonNegativeOrDefault(
                        tuning.promptEnchantmentCriticalHediffSeverity,
                        PawnDiary.PromptEnchantmentTuning.DefaultCriticalHediffSeverity),
                    cloudedConsciousnessBelow = NonNegativeOrDefault(
                        tuning.promptEnchantmentCloudedConsciousnessBelow,
                        PawnDiary.PromptEnchantmentTuning.DefaultCloudedConsciousnessBelow),
                    fadingConsciousnessBelow = NonNegativeOrDefault(
                        tuning.promptEnchantmentFadingConsciousnessBelow,
                        PawnDiary.PromptEnchantmentTuning.DefaultFadingConsciousnessBelow),
                    barelyConsciousBelow = NonNegativeOrDefault(
                        tuning.promptEnchantmentBarelyConsciousBelow,
                        PawnDiary.PromptEnchantmentTuning.DefaultBarelyConsciousBelow),
                    maxImpactCues = tuning.promptEnchantmentMaxImpactCues < 0
                        ? PawnDiary.PromptEnchantmentTuning.DefaultMaxImpactCues
                        : tuning.promptEnchantmentMaxImpactCues
                };
            }
        }

        private static float NonNegativeOrDefault(float value, float fallback)
        {
            return value < 0f || float.IsNaN(value) ? fallback : value;
        }

        private static int NonNegativeOrDefault(int value, int fallback)
        {
            return value < 0 ? fallback : value;
        }

        private static int PositiveOrDefault(int value, int fallback)
        {
            return value > 0 ? value : fallback;
        }
    }
}
