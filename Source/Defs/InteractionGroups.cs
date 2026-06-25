// The interaction-group catalog is data-driven: each group is a DiaryInteractionGroupDef
// loaded from XML (1.6/Defs/DiaryInteractionGroupDefs.xml), so groups, their matchers, and
// their diary prompts can be retuned by editing XML and restarting — no recompile. This file
// defines the Def type plus the static lookup/classification helpers over the DefDatabase.
// New to C#/RimWorld? See AGENTS.md ("Defs & DefDatabase").
using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace PawnDiary
{
    // Which kind of event a group classifies. Interaction groups match InteractionDefs
    // (social log entries); MentalState groups match MentalStateDefs (breaks, social fights);
    // Tale groups match TaleDefs (RimWorld's notable-history events); MoodEvent groups match
    // GameConditionDefs that affect colonist mood (aurora, eclipse, psychic drone, etc.);
    // Inspiration groups match InspirationDefs when a pawn gains an inspiration; Romance groups
    // match PawnRelationDef defNames for relation changes (Lover/Spouse/etc.); Work groups classify
    // the synthetic work diary events emitted by the periodic work scanner; Hediff groups match
    // HediffDefs when a health condition appears or worsens; Raid groups match raid incident plus
    // optional arrival/strategy tokens (RaidEnemy/RaidFriendly/Infestation/drop pods); Quest groups
    // match the quest lifecycle signal ("accepted"/"completed"/"failed") so one DiaryEventType.Quest
    // fans out to three groups; Ritual groups match Precept_Ritual defNames from finished Ideology rituals.
    // Ability groups match AbilityDef defNames/category tokens from successful Ability.Activate.
    // RimWorld parses this enum straight from XML text (e.g. <domain>MentalState</domain>).
    public enum GroupDomain
    {
        Interaction,
        MentalState,
        Tale,
        MoodEvent,
        Thought,
        Inspiration,
        Romance,
        Work,
        Hediff,
        Raid,
        Quest,
        Ritual,
        Ability
    }

    // How an XML batch is keyed. Pair means "one group-level batch" (per pawn pair for
    // Interaction rows, per pawn for Tale rows); Def keeps separate batches per source defName.
    public enum InteractionBatchScope
    {
        Pair,
        Def
    }

    // Which kind of diary event an Interaction-domain batch eventually produces.
    // PairEvent creates one merged pairwise event. AmbientDayNote treats the
    // low-stakes interactions as background material and writes one solo diary note per pawn/day.
    public enum InteractionBatchMode
    {
        PairEvent,
        AmbientDayNote
    }

    // Optional batching policy embedded in a DiaryInteractionGroupDef. RimWorld can populate this
    // from a nested <batch> XML node. Interaction-domain groups use the full policy; Tale-domain
    // groups use it for delayed per-pawn solo batches so bursts of combat tales do not flood the LLM.
    public class InteractionBatchPolicy
    {
        // Set false in XML to leave a documented policy disabled without deleting it.
        public bool enabled = true;
        // PairEvent = one merged pairwise event; AmbientDayNote = one solo low-stakes memory per pawn/day.
        public InteractionBatchMode mode = InteractionBatchMode.PairEvent;
        // How long the batch waits after the last matching event before flushing.
        public int windowTicks = 0;
        // Force a flush after this many accumulated moments.
        public int maxEvents = 1;
        // Pair = one group-level batch per pawn pair; Def = separate per InteractionDef per pair.
        public InteractionBatchScope scope = InteractionBatchScope.Pair;
        // Synthetic defName used for combined diary events. Empty falls back to "<group>Batch".
        public string syntheticDefName;
        // Localization keys used for combined label/text/instruction. Empty uses generic batch keys.
        public string labelKey;
        public string briefKey;
        public string headerKey;
        public string fallbackKey;
        public string instructionKey;
        // When true, each line is "Interaction label: original log text"; false keeps only log text.
        public bool includeInteractionLabel = true;
        // AmbientDayNote only: drop the note if fewer than this many moments happened.
        public int minEventsToWrite = 1;
        // AmbientDayNote only: keep at most this many evidence lines in the raw prompt text.
        public int maxSampleLines = 5;
    }

    // Optional "promotion" policy embedded in a DiaryInteractionGroupDef. A batched (low-value)
    // interaction normally gets merged into the daily ambient note, so individually-interesting
    // moments are lost. With a <promotion> node, each matching moment gets a weighted-random roll:
    // win and it escapes the batch to become its own immediate pairwise diary event; lose and it
    // batches as before. The odds are a small base chance plus bonuses when the moment looks
    // notable. Every signal reads structured, language-independent data (opinion numbers, need
    // levels) — no text/topic matching — so behavior is identical in any language.
    public class InteractionPromotionPolicy
    {
        // Set false in XML to leave a documented policy disabled without deleting it.
        public bool enabled = true;
        // Floor probability that any matching moment is promoted (0..1). Keep small so most stay filler.
        public float baseChance = 0.04f;
        // Hard ceiling on the probability after all bonuses are added (0..1).
        public float maxChance = 0.6f;

        // ---- Social-dynamic signal (reads relations.OpinionOf, both directions) ----
        // Added when the strongest opinion between the pair is at/above opinionStrongThreshold
        // (intense love or hate makes even idle chatter loaded).
        public float opinionStrongBonus = 0.25f;
        public int opinionStrongThreshold = 40;
        // Added when the two pawns' opinions of each other differ by at least this many points
        // (lopsided feelings — one adores, the other is cold).
        public float opinionAsymmetryBonus = 0.2f;
        public int opinionAsymmetryThreshold = 40;

        // ---- Pawn-state salience signal (reads need levels) ----
        // Added when either pawn has a core need (food/rest/joy) at/below needLowThreshold
        // (a starving or exhausted pawn's small talk is worth surfacing).
        public float needLowBonus = 0.2f;
        public float needLowThreshold = 0.25f;
        // Added when either pawn's mood is at/below moodLowThreshold (near a mental break).
        public float moodExtremeBonus = 0.2f;
        public float moodLowThreshold = 0.25f;
    }

    // How a Hediff-domain group writes a diary signal. DayReflection folds health changes into the
    // existing end-of-day reflection. Immediate writes a solo diary event as soon as the hediff
    // appears or crosses a configured severity step.
    public enum HediffDiaryMode
    {
        DayReflection,
        Immediate
    }

    // Optional hediff policy embedded in a DiaryInteractionGroupDef. It is intentionally generic:
    // XML chooses which HediffDef defNames belong to the group and this policy decides whether a
    // matching live Hediff is important enough to record. Compatibility packs for other mods should
    // add or patch Hediff-domain groups instead of requiring C# patches.
    public class HediffSignalPolicy
    {
        // Set false in XML to document a policy without enabling it.
        public bool enabled = true;
        // DayReflection = save as one candidate in the pawn's end-of-day reflection.
        // Immediate = write a solo diary page right away.
        public HediffDiaryMode mode = HediffDiaryMode.DayReflection;
        // Require hediff.Visible before recording. The default is true for compatibility packs; the
        // built-in fallback group overrides this to preserve the old major-affliction behavior.
        public bool visibleOnly = true;
        // Require HediffDef.isBad. Specific positive/neutral modded hediff groups can set false.
        public bool badOnly = false;
        // Plain injuries usually already have Tale/combat coverage, so groups can ignore them.
        public bool excludeInjuries = true;
        // Base severity gate. Special "always" flags below can still qualify lower-severity hediffs.
        public float minSeverity = 0f;
        public bool chronicAlways = false;
        public bool sickThoughtAlways = false;
        public bool addictionAlways = false;
        public bool missingPartAlways = false;
        // Event triggers. recordOnAdd is driven by Pawn_HealthTracker.AddHediff; severity increases
        // are found by the periodic scanner.
        public bool recordOnAdd = true;
        public bool recordOnSeverityIncrease = false;
        // Severity step size for progression recording. A step of 0 disables progression recording
        // even if recordOnSeverityIncrease is true.
        public float severityStep = 0.25f;
        // Dedup window for the same pawn/hediff/source/stage.
        public int dedupTicks = 60000;
        // Relative weight when this hediff becomes an end-of-day reflection candidate.
        public float dayReflectionWeight = 0.8f;
        // Optional Keyed fallback text for Immediate mode. Keys receive pawn label and hediff label
        // as {0} and {1}; blank falls back to the generic health-condition text.
        public string appearedTextKey;
        public string progressedTextKey;
    }

    // A themed bucket of events, loaded from XML as a RimWorld Def. Each group is one row in
    // settings: an enable toggle (is it recorded?) plus a single diary-prompt instruction shared by
    // every event in it. To add or retune a group, edit
    // 1.6/Defs/DiaryInteractionGroupDefs.xml — no code change needed.
    //
    // `Def` (the base class) already supplies two fields we rely on:
    //   - defName : the stable key (e.g. "romance"). Settings store per-group overrides under it,
    //               so renaming a defName would orphan a player's saved settings.
    //   - label   : the human-readable name shown in the settings UI.
    public class DiaryInteractionGroupDef : Def
    {
        // Whether events in this group are recorded by default (a player can override per-save).
        public bool defaultEnabled = true;

        // Whether entries from this group should be visually marked as important in the Diary tab.
        // Low-stakes groups can set this false in XML without changing save data or code.
        public bool important = true;

        // Whether events in this group are combat-related (social fights, insults). Used to decide
        // whether to add the equipped weapon to the prompt; set per group in XML, default false.
        public bool combat = false;

        // Optional batching policy. Interaction groups can merge or ambient-batch social log rows;
        // selected Tale groups can delay and merge bursts into one solo event per pawn.
        public InteractionBatchPolicy batch;

        // Optional promotion policy. When present and enabled on a batch group, each matching moment
        // can win a weighted-random roll to skip batching and become its own immediate pairwise
        // event (see InteractionPromotionPolicy). Only consulted for groups that also batch.
        public InteractionPromotionPolicy promotion;

        // Optional Hediff-domain policy. When present on a Hediff group, matching live hediffs can
        // become day-reflection signals or immediate solo diary entries without any per-mod C# hook.
        public HediffSignalPolicy hediff;

        // Optional Tale-domain death metadata. Vanilla death TaleDefs are still just TaleDefs, but
        // different defs put the victim in different pawn slots. Keeping those lists in XML means new
        // or corrected death tales do not need a code edit.
        public List<string> deathVictimInitiatorDefNames;
        public List<string> deathVictimRecipientDefNames;

        // The diary-prompt instruction shared by every event in the group. This is the event-type
        // prompt: it tells the model what kind of moment this is.
        public string instruction;

        // Optional variant pool for `instruction`. When this list has any non-blank entry, one
        // wording is picked per captured event (see PromptVariants.Pick) so the Nth entry of a kind
        // does not read identically to the first. The singular `instruction` above is the fallback
        // used when the pool is absent/empty, and stays the value shown in the settings preview.
        // Localized via DefInjected like `instruction`. IMPORTANT: do not leave blank <li> slots in
        // this list — variant selection skips whitespace entries, which would misalign indexed
        // DefInjected translation keys (<group.instructions.0>, .1, ...). Remove unused entries.
        public List<string> instructions;

        // Optional emotional register for entries in this group (e.g. "with creeping dread"). This
        // is event-driven: a raid reads tense, a prank light. Sent to the LLM as a "tone:" field for
        // first-person entries; empty leaves the tone neutral.
        // Localized via DefInjected like `instruction` (it reaches the prompt), not Keyed.
        public string tone;

        // Optional variant pool for `tone`, mirroring `instructions`. One wording is picked per
        // prompt build, deterministically seeded by the event id so the same entry keeps the same
        // tone across save/load and regeneration. Same no-blank-<li> rule as `instructions`.
        public List<string> tones;

        // Optional stable UI color cue stored on new DiaryEvents. This is deliberately an internal
        // key (for example "combat" or "socialFight"), not translated player-facing text.
        public string colorCue;

        // Which event source this group classifies. Classification is scoped to a domain so
        // unrelated Def types with the same defName never cross-match.
        public GroupDomain domain = GroupDomain.Interaction;

        // Exact defName matches (case-insensitive). Optional in XML.
        public List<string> matchDefNames;

        // Substring tokens: a defName that contains any token (case-insensitive) matches. Optional.
        public List<string> matchTokens;

        // When true this group matches everything in its domain (the catch-all). Give it the
        // highest `order` in its domain so the specific groups get first claim.
        public bool catchAll = false;

        // Classification order within a domain: lower numbers are tested first ("first match
        // wins"). Def load order across files is not guaranteed, so this keeps it deterministic.
        public int order = 0;

        // True if this group claims the given defName. Check order mirrors the old catalog:
        // catch-all, then exact defNames, then substring tokens.
        public bool Matches(string defName)
        {
            if (catchAll)
            {
                return true;
            }

            if (string.IsNullOrEmpty(defName))
            {
                return false;
            }

            if (batch != null
                && !string.IsNullOrWhiteSpace(batch.syntheticDefName)
                && string.Equals(batch.syntheticDefName, defName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (matchDefNames != null)
            {
                for (int i = 0; i < matchDefNames.Count; i++)
                {
                    if (string.Equals(matchDefNames[i], defName, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }

            if (matchTokens != null)
            {
                for (int i = 0; i < matchTokens.Count; i++)
                {
                    if (defName.IndexOf(matchTokens[i], StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        // True when this group should batch matching InteractionDef events. Kept as a helper so
        // callers don't need to know how a missing <batch> node is represented.
        public bool HasBatchPolicy
        {
            get
            {
                return domain == GroupDomain.Interaction && batch != null && batch.enabled;
            }
        }

        // True when this Tale-domain group should delay matching TaleDefs into per-pawn solo batches.
        public bool HasTaleBatchPolicy
        {
            get
            {
                return domain == GroupDomain.Tale && batch != null && batch.enabled;
            }
        }

        // True when this group can promote a batched moment into its own immediate event. Promotion
        // only makes sense as an escape hatch from batching, so it requires an active batch policy.
        public bool HasPromotionPolicy
        {
            get
            {
                return HasBatchPolicy && promotion != null && promotion.enabled;
            }
        }

        // True when this group can record HediffDef matches through the generic health-signal layer.
        public bool HasHediffPolicy
        {
            get
            {
                return domain == GroupDomain.Hediff && hediff != null && hediff.enabled;
            }
        }

        // Returns which Tale pawn slot contains the death victim, or empty for non-death tales.
        public string DeathVictimRoleFor(string defName)
        {
            if (domain != GroupDomain.Tale || string.IsNullOrWhiteSpace(defName))
            {
                return string.Empty;
            }

            if (InteractionGroups.ContainsDefName(deathVictimInitiatorDefNames, defName))
            {
                return DiaryEvent.InitiatorRole;
            }

            if (InteractionGroups.ContainsDefName(deathVictimRecipientDefNames, defName))
            {
                return DiaryEvent.RecipientRole;
            }

            return string.Empty;
        }
    }

    // Static lookup + classification over the loaded DiaryInteractionGroupDefs. (A static class
    // is a namespace-level singleton — no instances; see AGENTS.md.)
    public static class InteractionGroups
    {
        private static List<DiaryInteractionGroupDef> cachedAll;

        // All groups, sorted by `order` so "first match wins" is deterministic. Cached after the
        // first call because Defs are loaded once at startup and don't change during play.
        public static List<DiaryInteractionGroupDef> All
        {
            get
            {
                if (cachedAll == null)
                {
                    cachedAll = DefDatabase<DiaryInteractionGroupDef>.AllDefsListForReading
                        .OrderBy(group => group.order)
                        .ToList();
                }

                return cachedAll;
            }
        }

        // First Interaction-domain group that matches the interaction, else the Interaction
        // catch-all ("Other").
        public static DiaryInteractionGroupDef Classify(InteractionDef interactionDef)
        {
            return ClassifyIn(GroupDomain.Interaction, interactionDef?.defName);
        }

        // First MentalState-domain group that matches the state, else the MentalState catch-all
        // ("Mental breaks").
        public static DiaryInteractionGroupDef ClassifyMentalState(MentalStateDef stateDef)
        {
            return ClassifyIn(GroupDomain.MentalState, stateDef?.defName);
        }

        // First Tale-domain group that matches the tale, else the Tale catch-all.
        public static DiaryInteractionGroupDef ClassifyTale(TaleDef taleDef)
        {
            return ClassifyIn(GroupDomain.Tale, taleDef?.defName);
        }

        // First MoodEvent-domain group that matches the GameConditionDef, else the MoodEvent catch-all.
        public static DiaryInteractionGroupDef ClassifyMoodEvent(GameConditionDef conditionDef)
        {
            return ClassifyIn(GroupDomain.MoodEvent, conditionDef?.defName);
        }

        // First Thought-domain group that matches the ThoughtDef, else the Thought catch-all.
        public static DiaryInteractionGroupDef ClassifyThought(ThoughtDef thoughtDef)
        {
            return ClassifyIn(GroupDomain.Thought, thoughtDef?.defName);
        }

        // First Inspiration-domain group that matches the InspirationDef, else the Inspiration catch-all.
        public static DiaryInteractionGroupDef ClassifyInspiration(InspirationDef inspirationDef)
        {
            return ClassifyIn(GroupDomain.Inspiration, inspirationDef?.defName);
        }

        // First Romance-domain group that explicitly matches the PawnRelationDef defName. Unlike
        // saved-event display classification, live capture must not fall back to a catch-all here:
        // only XML-listed relation changes should create romance diary entries.
        public static DiaryInteractionGroupDef ClassifyRomanceRelation(string relationDefName)
        {
            return ClassifyRequiredMatch(GroupDomain.Romance, relationDefName);
        }

        // First Work-domain group that matches the synthetic work diary defName.
        public static DiaryInteractionGroupDef ClassifyWork(string workEventDefName)
        {
            return ClassifyIn(GroupDomain.Work, workEventDefName);
        }

        // First Hediff-domain group that matches a health condition, else the Hediff catch-all.
        public static DiaryInteractionGroupDef ClassifyHediff(HediffDef hediffDef)
        {
            return ClassifyIn(GroupDomain.Hediff, hediffDef?.defName);
        }

        // First Raid-domain group that matches the raid classifier string (incident defName plus
        // optional arrival/strategy tokens), else the Raid catch-all ("Raids"). Specific groups are
        // ordered before the catch-all so drop pods, infestations, and friendly arrivals can claim
        // their own prompt policy.
        public static DiaryInteractionGroupDef ClassifyRaid(string incidentDefName)
        {
            return ClassifyIn(GroupDomain.Raid, incidentDefName);
        }

        // First Quest-domain group that matches the lifecycle signal. The signal IS the classifier
        // key: "accepted" -> questAccepted, "completed" -> questCompleted, "failed" -> questFailed.
        // There is intentionally no catch-all: an unknown signal returns the last Quest group
        // (ClassifyIn fallback), but the hook layer only ever passes one of the three known signals.
        public static DiaryInteractionGroupDef ClassifyQuest(string signal)
        {
            return ClassifyIn(GroupDomain.Quest, signal);
        }

        // First Ritual-domain group that matches the Precept_Ritual defName plus optional behavior
        // worker class, else the Ritual catch-all. Ritual-specific packs can add exact
        // defName/token groups without C# changes.
        public static DiaryInteractionGroupDef ClassifyRitual(string ritualClassifierKey)
        {
            return ClassifyIn(GroupDomain.Ritual, ritualClassifierKey);
        }

        // First Ability-domain group that matches the AbilityDef classifier key, else the Ability
        // catch-all. The key can include category/source tokens as well as the defName.
        public static DiaryInteractionGroupDef ClassifyAbility(string abilityClassifierKey)
        {
            return ClassifyIn(GroupDomain.Ability, abilityClassifierKey);
        }

        // Same classifier, but for saved events where we only have the stored defName string.
        // The Diary tab and save migration helpers use this to recover labels, importance, and
        // semantic color cues for older entries.
        public static DiaryInteractionGroupDef ClassifyDefName(GroupDomain domain, string defName)
        {
            return ClassifyIn(domain, defName);
        }

        private static DiaryInteractionGroupDef ClassifyRequiredMatch(GroupDomain domain, string defName)
        {
            if (string.IsNullOrEmpty(defName))
            {
                return null;
            }

            List<DiaryInteractionGroupDef> all = All;
            for (int i = 0; i < all.Count; i++)
            {
                DiaryInteractionGroupDef group = all[i];
                if (group.domain == domain && group.Matches(defName))
                {
                    return group;
                }
            }

            return null;
        }

        private static DiaryInteractionGroupDef ClassifyIn(GroupDomain domain, string defName)
        {
            DiaryInteractionGroupDef fallback = null;
            List<DiaryInteractionGroupDef> all = All;
            for (int i = 0; i < all.Count; i++)
            {
                DiaryInteractionGroupDef group = all[i];
                if (group.domain != domain)
                {
                    continue;
                }

                if (group.Matches(defName))
                {
                    return group;
                }

                fallback = group;
            }

            return fallback;
        }

        // Look up a group by its defName/key (used to read per-group settings). Null if absent.
        public static DiaryInteractionGroupDef ByKey(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return null;
            }

            return DefDatabase<DiaryInteractionGroupDef>.GetNamedSilentFail(key);
        }

        // ---- Prompt instruction resolution ----
        //
        // The methods below resolve the diary-prompt instruction for a captured event. Each one
        // classifies the incoming Def/string into its DiaryInteractionGroupDef (using the Classify*
        // helpers above) and then rolls one wording from the group's variant pool. They used to live
        // on PawnDiarySettings, but they read NO settings state — instructions are XML-only now (no
        // saved overrides) — so they belong here, beside classification, and are static.
        //
        // Roll timing: Rand is RimWorld's main-thread RNG. Capture callers freeze the returned
        // wording straight into diaryEvent.instruction, so a fresh roll per event is correct (the
        // same event keeps the same wording after save/load because it is persisted on the event).
        // The settings preview reads group.instruction (the singular fallback) directly to avoid
        // flicker — see PawnDiarySettings.EditableInstructionForGroup.

        // One rolled instruction wording for a group already classified. When the group defines an
        // instructions variant pool, one wording is rolled; otherwise the singular instruction
        // fallback is used. Prompt wording is XML-only, so tuning stays in Defs, not saves.
        public static string InstructionForGroup(DiaryInteractionGroupDef group)
        {
            if (group == null)
            {
                return string.Empty;
            }

            return PromptVariants.Pick(group.instructions, group.instruction, Rand.Range(0, int.MaxValue));
        }

        // Interaction-domain (social log) instruction.
        public static string InstructionFor(InteractionDef interactionDef)
        {
            if (interactionDef == null)
            {
                return string.Empty;
            }

            return InstructionForGroup(Classify(interactionDef));
        }

        // MentalState-domain (social fights, mental breaks) instruction.
        public static string InstructionForMentalState(MentalStateDef stateDef)
        {
            if (stateDef == null)
            {
                return string.Empty;
            }

            return InstructionForGroup(ClassifyMentalState(stateDef));
        }

        // Tale-domain (notable history: deaths, injuries, recruitment, disasters, ...) instruction.
        public static string InstructionForTale(TaleDef taleDef)
        {
            if (taleDef == null)
            {
                return string.Empty;
            }

            return InstructionForGroup(ClassifyTale(taleDef));
        }

        // MoodEvent-domain (aurora, eclipse, psychic drone, toxic fallout, ...) instruction.
        public static string InstructionForMoodEvent(GameConditionDef conditionDef)
        {
            if (conditionDef == null)
            {
                return string.Empty;
            }

            return InstructionForGroup(ClassifyMoodEvent(conditionDef));
        }

        // Thought-domain (expiring positive/negative mood thoughts) instruction.
        public static string InstructionForThought(ThoughtDef thoughtDef)
        {
            if (thoughtDef == null)
            {
                return string.Empty;
            }

            return InstructionForGroup(ClassifyThought(thoughtDef));
        }

        // Inspiration-domain instruction.
        public static string InstructionForInspiration(InspirationDef inspirationDef)
        {
            if (inspirationDef == null)
            {
                return string.Empty;
            }

            return InstructionForGroup(ClassifyInspiration(inspirationDef));
        }

        // Work-domain instruction. The scanner picks the group first (passion, strain, routine,
        // dark study) because those groups depend on pawn state as well as the WorkTypeDef.
        public static string InstructionForWork(DiaryInteractionGroupDef group)
        {
            return InstructionForGroup(group);
        }

        // Hediff-domain instruction (group's XML default).
        public static string InstructionForHediff(HediffDef hediffDef)
        {
            if (hediffDef == null)
            {
                return string.Empty;
            }

            return InstructionForGroup(ClassifyHediff(hediffDef));
        }

        // Raid-domain instruction (group's XML default).
        public static string InstructionForRaid(string incidentDefName)
        {
            if (string.IsNullOrEmpty(incidentDefName))
            {
                return string.Empty;
            }

            return InstructionForGroup(ClassifyRaid(incidentDefName));
        }

        // Quest-domain instruction. The signal ("accepted"/"completed"/"failed") is the key.
        public static string InstructionForQuest(string signal)
        {
            if (string.IsNullOrEmpty(signal))
            {
                return string.Empty;
            }

            return InstructionForGroup(ClassifyQuest(signal));
        }

        // Case-insensitive exact defName membership shared across impure classifiers
        // (DiaryInteractionGroupDef.DeathVictimRoleFor, MoodImpactClassifier's known-condition
        // fallbacks). Null/empty defName and null/empty list never match. Promoted here so the
        // Ordinal-IgnoreCase scan has one home instead of being re-implemented per caller.
        internal static bool ContainsDefName(List<string> defNames, string defName)
        {
            if (string.IsNullOrEmpty(defName) || defNames == null)
            {
                return false;
            }

            for (int i = 0; i < defNames.Count; i++)
            {
                if (string.Equals(defNames[i], defName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
