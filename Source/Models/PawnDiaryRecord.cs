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

        // Optional external writing-style override. When set, it sits above the pawn's base style and
        // temporary hediff style overrides until the owning source clears it through the integration API.
        public string externalWritingStyleOverrideRule;
        public string externalWritingStyleOverrideSourceId;

        // Optional pawn-specific custom writing-style prompt. Blank means "use the selected base style";
        // nonblank means "use this prompt when no higher-priority override is active." It lives on the
        // pawn's own record, so it never touches DiaryPersonaDef XML or global PersonaPresetStore settings.
        // The effective priority is External API override > Hediff override > Pawn custom > Base style.
        public string customWritingStyleRule;

        // ---- Psychotype (outlook) layer: the second per-pawn voice, sibling to the style above ----
        // Which DiaryPsychotypeDef this pawn uses. Empty string means "unset": either a pre-feature
        // legacy record or a new record whose roll is still pending. The lazy generation-path check
        // (DiaryGameComponent.EnsureVoiceStage) fills it in — Neutral for established pre-feature voices,
        // a fresh band-appropriate roll otherwise — so old saves are always given a valid psychotype.
        public string psychotypeDefName;

        // Optional external integration psychotype override, sitting above the pawn's base/custom rule.
        public string externalPsychotypeOverrideRule;
        public string externalPsychotypeOverrideSourceId;

        // Optional pawn-specific custom psychotype rule (player-authored, keeps line breaks). Priority is
        // External API override > Pawn custom > Base type (no hediff psychotype layer in v1).
        public string customPsychotypeRule;

        // Which catalog band ("child"/"adult") this record's current voice rolls were made for. Empty
        // means a pre-feature legacy record whose band has not been stamped yet. When the pawn crosses
        // psychotypeCrystallizationAgeYears the band mismatches and both unpinned layers re-roll ("crystallize").
        public string voiceStageBand;

        // Player-made picks are pinned and never auto-re-rolled (e.g. when the pawn grows up). Set when
        // the player manually chooses/re-rolls/edits a layer from the per-pawn editor.
        public bool psychotypePinned;
        public bool writingStylePinned;

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

        // Pages the player starred as favorites from the Diary tab, keyed by the same stable UI entry
        // key ("eventId|povRole") the card renderer uses. Additive save key; old saves load an empty
        // list. Keys are only ever removed by the player un-starring a page, so a stale key left by a
        // pruned/archived event is harmless — it simply never matches a visible card again.
        public List<string> favoriteEntryKeys = new List<string>();

        // Scanner bookkeeping for pawn progression pages. Additive save key; old saves create a
        // baseline-pending state so they do not emit catch-up milestone pages on first load.
        public PawnProgressionState progressionState;

        // Rare life-arc reflection cadence and memory repetition control. This is scheduling state,
        // not a separate pawn-history store; existing diary pages remain the history layer.
        public PawnArcScheduleState arcSchedule;

        // Passive Ideology observation and future reflection bookkeeping. The deep object's default
        // requests a silent baseline, so old saves never receive catch-up belief pages.
        public PawnBeliefState beliefState;

        // Unified N4 reflection cooldown and one-request major-arc queue. A missing deep row means this
        // record predates N4 and must baseline silently at its first natural reflection opportunity.
        public PawnReflectionState reflectionState;

        // Deterministic knowledge state (design/MEMORY_SYSTEM_REDESIGN_PLAN.md): origin/adopted
        // culture plus lifelong important-event records. A missing deep row means the record
        // predates the redesign; history starts from the update onward (§6).
        public PawnKnowledgeState knowledgeState;

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
            Scribe_Values.Look(ref externalWritingStyleOverrideRule, "externalWritingStyleOverrideRule");
            Scribe_Values.Look(ref externalWritingStyleOverrideSourceId, "externalWritingStyleOverrideSourceId");
            Scribe_Values.Look(ref customWritingStyleRule, "customWritingStyleRule");
            // Psychotype layer. No default on psychotypeDefName / voiceStageBand so a missing (legacy)
            // value loads as null: the component's lazy defaults pass detects "unset" and backfills it.
            Scribe_Values.Look(ref psychotypeDefName, "psychotypeDefName");
            Scribe_Values.Look(ref externalPsychotypeOverrideRule, "externalPsychotypeOverrideRule");
            Scribe_Values.Look(ref externalPsychotypeOverrideSourceId, "externalPsychotypeOverrideSourceId");
            Scribe_Values.Look(ref customPsychotypeRule, "customPsychotypeRule");
            Scribe_Values.Look(ref voiceStageBand, "voiceStageBand");
            Scribe_Values.Look(ref psychotypePinned, "psychotypePinned", false);
            Scribe_Values.Look(ref writingStylePinned, "writingStylePinned", false);
            Scribe_Values.Look(ref diaryGenerationEnabled, "diaryGenerationEnabled", true);
            Scribe_Values.Look(ref acknowledgedGeneratedEntryCount, "acknowledgedGeneratedEntryCount", -1);
            Scribe_Values.Look(ref hasUnreadGeneratedEntry, "hasUnreadGeneratedEntry", false);
            Scribe_Collections.Look(ref eventIds, "eventIds", LookMode.Value);
            Scribe_Collections.Look(ref favoriteEntryKeys, "favoriteEntryKeys", LookMode.Value);
            Scribe_Deep.Look(ref progressionState, "progressionState");
            Scribe_Deep.Look(ref arcSchedule, "arcSchedule");
            Scribe_Deep.Look(ref beliefState, "beliefState");
            Scribe_Deep.Look(ref reflectionState, "reflectionState");
            Scribe_Deep.Look(ref knowledgeState, "knowledgeState");

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                pawnId = pawnId ?? string.Empty;
                pawnName = pawnName ?? string.Empty;

                // Recover gracefully if a style Def was renamed/removed.
                if (string.IsNullOrWhiteSpace(personaDefName) || DiaryPersonas.ForDefName(personaDefName) == null)
                {
                    personaDefName = DiaryPersonas.Default.defName;
                }

                externalWritingStyleOverrideRule = ExternalWritingStyleOverrideText.CleanRule(
                    externalWritingStyleOverrideRule);
                externalWritingStyleOverrideSourceId = ExternalWritingStyleOverrideText.CleanSourceId(
                    externalWritingStyleOverrideSourceId);
                if (string.IsNullOrWhiteSpace(externalWritingStyleOverrideRule))
                {
                    externalWritingStyleOverrideRule = string.Empty;
                    externalWritingStyleOverrideSourceId = string.Empty;
                }

                // Player-authored custom prompt keeps its line breaks (so the editor stays readable),
                // so it uses the multiline sanitizer rather than the one-line external override cleaner.
                customWritingStyleRule = PlayerWritingStyleText.CleanRule(customWritingStyleRule);

                // ---- Psychotype layer ----
                // A named psychotype whose Def was removed/renamed falls back to Neutral so the prompt
                // block is simply omitted; a blank value is left blank on purpose (the "unset" marker the
                // component's lazy backfill looks for, so old saves are never crashed by a missing field).
                if (!string.IsNullOrWhiteSpace(psychotypeDefName) && DiaryPsychotypes.ForDefName(psychotypeDefName) == null)
                {
                    psychotypeDefName = DiaryPsychotypes.NeutralDefName;
                }

                psychotypeDefName = psychotypeDefName ?? string.Empty;
                voiceStageBand = voiceStageBand ?? string.Empty;
                customPsychotypeRule = PsychotypeText.CleanRule(customPsychotypeRule);
                externalPsychotypeOverrideRule = PsychotypeText.CleanExternalRule(externalPsychotypeOverrideRule);
                externalPsychotypeOverrideSourceId = PsychotypeText.CleanSourceId(externalPsychotypeOverrideSourceId);
                if (string.IsNullOrWhiteSpace(externalPsychotypeOverrideRule))
                {
                    externalPsychotypeOverrideRule = string.Empty;
                    externalPsychotypeOverrideSourceId = string.Empty;
                }

                if (eventIds == null)
                {
                    eventIds = new List<string>();
                }

                // A hand-edited/interrupted save can contain duplicates, blanks, or an extreme list.
                // Normalize through the pure O(n) policy so loading remains bounded before the UI ever
                // mirrors these keys. Old saves with no field become the same non-null empty list.
                favoriteEntryKeys = DiaryEntryFilterPolicy.NormalizeFavoriteEntryKeys(favoriteEntryKeys);

                EnsureProgressionState();
                EnsureArcSchedule();
                EnsureBeliefState();
                EnsureReflectionState();
                EnsureKnowledgeState();
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

        /// <summary>Returns normalized passive belief state, creating old-save defaults as needed.</summary>
        public PawnBeliefState EnsureBeliefState()
        {
            if (beliefState == null)
            {
                beliefState = new PawnBeliefState();
            }

            // PostLoadInit normally has a game clock, but detached Scribe fixtures and unusual load
            // ordering may not. Deferring age-based cleanup is safer than treating "no clock" as
            // int.MaxValue, which would erase otherwise valid pending certainty evidence.
            int? now = Find.TickManager?.TicksGame;
            if (now.HasValue)
            {
                beliefState.Normalize(now.Value, DiaryBeliefPolicy.Snapshot());
            }

            return beliefState;
        }

        /// <summary>The passive belief state without creating or normalizing one.</summary>
        public PawnBeliefState BeliefStateOrNull()
        {
            return beliefState;
        }

        /// <summary>Returns normalized N4 runtime state, creating an old-save silent baseline if absent.</summary>
        public PawnReflectionState EnsureReflectionState()
        {
            if (reflectionState == null)
            {
                reflectionState = new PawnReflectionState();
            }

            reflectionState.Normalize();
            return reflectionState;
        }

        /// <summary>
        /// Returns normalized knowledge state (culture + important-event records,
        /// design/MEMORY_SYSTEM_REDESIGN_PLAN.md), creating a fresh one for old saves — the
        /// redesign's clean start deliberately migrates nothing from the associative system.
        /// </summary>
        public PawnKnowledgeState EnsureKnowledgeState()
        {
            if (knowledgeState == null)
            {
                knowledgeState = new PawnKnowledgeState { pawnId = pawnId ?? string.Empty };
            }

            knowledgeState.Normalize();
            return knowledgeState;
        }

        /// <summary>The knowledge state without creating one — read-only inspection paths.</summary>
        public PawnKnowledgeState KnowledgeStateOrNull()
        {
            return knowledgeState;
        }
    }
}
