// Observed-condition defs (Plan 12): XML-controlled policy for *lasting* colony states read from live
// game state, as opposed to the one-shot signal windows in DiaryEventWindowDef. Each def names one
// observer (what live state to read), how to debounce start/end, whether to record diary pages, and how
// strongly it biases prompt context while active. The actual lifecycle is decided by the pure
// ObservedConditionPolicy; this Def just supplies XML-owned policy. New to C#/RimWorld? See AGENTS.md
// ("Defs"). DLC-safety: matchers are plain strings, so absent DLC/mod content simply never matches.
using System;
using System.Collections.Generic;
using Verse;

namespace PawnDiary
{
    /// <summary>
    /// Which live source an observed condition reads. The scanner dispatches on this; absent content is
    /// safe because every observer matches by plain string / vanilla API that no-ops when empty.
    /// </summary>
    public enum ObservedConditionObserverType
    {
        // Active while a (home) map's danger rating / spawned-hostile count crosses thresholds.
        MapDanger,
        // Active while the map's GameConditionManager holds a matching condition defName.
        GameCondition,
        // Active while matching spawned things/filth remain on a map (observable evidence).
        ThingPresent,
        // Active while a matching visible hediff is present on a colonist (pawn-scoped).
        PawnHediff,
        // Active while a colonist is haunted by an unnatural corpse (Anomaly; pawn-scoped). The matcher
        // is the DLC's own tracker (GameComponent_Anomaly.PawnHasUnnaturalCorpse), NOT a defName list,
        // so this observer needs no matchDefNames and no-ops cleanly without the Anomaly DLC.
        PawnUnnaturalCorpse,
        // Active while ANY home-map colonist carries a matching hediff INCLUDING hidden ones, sensed as a
        // single map-level boolean. Tone-only by contract: the collector never feeds the hediff/host into
        // evidence, so a Def can read hidden state (e.g. an undiscovered infection) to color prompts
        // WITHOUT revealing the hidden mechanic. Use only with matchDefNames; treat the result as "the
        // colony is/isn't in this state", not "who has it".
        MapHiddenHediff,
        // Bounded fallback: a recent signal/letter, given a TTL and labelled "recent evidence".
        // Defined for completeness; no live scanner feeds it yet (see the Event System pages in repowiki/).
        RecentEvidence
    }

    /// <summary>
    /// Who receives the optional start/end diary page when an observed condition records one.
    /// </summary>
    public enum ObservedConditionRecordScope
    {
        MapColonists,
        SubjectPawn
    }

    /// <summary>
    /// XML-owned policy for one observed condition.
    /// </summary>
    public class DiaryObservedConditionDef : Def
    {
        public bool enabled = true;

        // Stable runtime key, shared across renamed defs. Defaults to defName.
        public string conditionKey;
        public ObservedConditionScope scope = ObservedConditionScope.Map;
        public ObservedConditionObserverType observerType = ObservedConditionObserverType.MapDanger;

        // How often this condition is polled. The component checks due-ness on a short global gate; this
        // is the real per-condition cadence (use a conservative value for expensive thing/hediff scans).
        public int pollIntervalTicks = 1000;

        // Debounce: how long a new observation must persist before its start is recorded, and how long a
        // missing observation must stay missing before its end is recorded. Ticks gate recording only —
        // never truth. 60000 ticks = one RimWorld day.
        public int startDebounceTicks = 0;
        public int endDebounceTicks = 2500;
        // Dedup window for recorded pages, so the same start/end cannot double-write.
        public int dedupTicks = 2500;

        // Optional diary pages. Default off: most lasting states only guide prompt tone. XML opts in.
        public bool recordStartEvent = false;
        public bool recordEndEvent = false;
        public ObservedConditionRecordScope recordScope = ObservedConditionRecordScope.MapColonists;
        public string startTextKey;
        public string endTextKey;
        // Literal settings overrides for optional start/end diary text. Placeholders mirror the Keyed
        // strings: {0}=pawn label, {1}=condition/evidence label.
        public string startText;
        public string endText;
        public string instruction;
        public string colorCue;

        // Prompt biasing while active (mirrors DiaryEventWindowDef).
        public bool promptEnabled = true;
        public float promptWeight = 1f;
        public float normalPromptWeightMultiplier = 1f;
        // Optional fade for long-lived conditions. 0 ticks means no decay; otherwise the prompt weight
        // and any normal-context override move toward this minimum as the condition ages.
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

        // ---- Matchers (plain strings: DLC/mod safe) ----
        // Exact defName matches (game conditions / things / hediffs). Preferred.
        public List<string> matchDefNames = new List<string>();
        // Case-insensitive substring fallback. Off by default; only for content with no stable defName.
        public List<string> matchDefNameContains = new List<string>();
        // Label substring fallback. Avoid unless there is genuinely no stable defName.
        public List<string> matchLabels = new List<string>();
        // ThingPresent-only suppression: if any listed ThingDef is currently spawned on the same map,
        // the observer reports this condition as absent. This lets XML say "gray-flesh suspicion ends
        // once a visible metalhorror exists" without hardcoding Anomaly names in C#.
        public List<string> suppressWhenThingDefNames = new List<string>();

        // ---- MapDanger tuning ----
        // Minimum StoryDanger to count as active: "None", "Low", or "High". Parsed by the scanner.
        public string minDangerRating = "Low";
        public int minHostileCount = 0;
        public bool includeHomeMapsOnly = true;
        public bool includeNonPlayerMaps = false;

        // ---- ThingPresent / RecentEvidence caps ----
        public int maxEvidenceLabels = 3;
        public int maxEvidenceChars = 200;
        // Defensive cap so a flooded map (lots of filth) never makes counting expensive.
        public int maxEvidenceCount = 999;
        public int recentEvidenceTtlTicks = 60000;
        // Optional hard cap for an observed condition that should not keep prompt pressure forever
        // even if its evidence remains. 0 means no cap; positive values force an end after this age.
        public int maxActiveTicks = 0;
        // After a condition is ended/dropped, block the same identity from restarting for this long.
        // Useful when XML suppression force-stops a condition while its original evidence can linger.
        public int restartCooldownTicks = 0;

        /// <summary>Stable key for runtime state; falls back to defName when XML omits it.</summary>
        public string EffectiveConditionKey()
        {
            return string.IsNullOrWhiteSpace(conditionKey) ? defName : conditionKey;
        }

        public int EffectivePollIntervalTicks()
        {
            return Math.Max(1, pollIntervalTicks);
        }

        public int EffectiveDedupTicks()
        {
            return dedupTicks < 0 ? 0 : dedupTicks;
        }

        public int EffectiveRestartCooldownTicks()
        {
            return restartCooldownTicks < 0 ? 0 : restartCooldownTicks;
        }

        public int EffectiveMaxActiveTicks()
        {
            return maxActiveTicks < 0 ? 0 : maxActiveTicks;
        }

        /// <summary>Projects the timing policy the pure planner needs.</summary>
        internal ObservedConditionDefSnapshot ToDefSnapshot()
        {
            return ObservedConditionDefSnapshot.Create(
                EffectiveConditionKey(), startDebounceTicks, endDebounceTicks);
        }

        /// <summary>
        /// Load-time validation (the game surfaces these as red errors in the log). Catches
        /// scope/record-scope combinations that would silently never record a page.
        /// </summary>
        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors())
            {
                yield return error;
            }

            // recordScope=SubjectPawn resolves the page recipient from state.subjectPawnId, but every
            // non-Pawn scope clears that id when building an observation (see NewObservation). Combined
            // they would yield an empty subject on every transition and silently never record a page.
            if (recordScope == ObservedConditionRecordScope.SubjectPawn
                && scope != ObservedConditionScope.Pawn)
            {
                yield return "recordScope=SubjectPawn requires scope=Pawn; any other scope has no subject pawn.";
            }

            // includeHomeMapsOnly short-circuits map eligibility (see MapEligible), so includeNonPlayerMaps
            // is dead when both are set. Surface the conflict instead of silently preferring home-only.
            if (includeHomeMapsOnly && includeNonPlayerMaps)
            {
                yield return "includeHomeMapsOnly=true makes includeNonPlayerMaps=true ineffective; "
                    + "set includeHomeMapsOnly=false to observe non-player maps.";
            }
        }
    }
}
