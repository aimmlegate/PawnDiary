// The interaction-group catalog is data-driven: each group is a DiaryInteractionGroupDef
// loaded from XML (1.6/Defs/DiaryInteractionGroupDefs.xml), so groups, their matchers, and
// their diary prompts can be retuned by editing XML and restarting — no recompile. This file
// defines the Def type plus the static lookup/classification helpers over the DefDatabase.
// New to C#/RimWorld? See AGENTS.md ("Defs & DefDatabase").
using System;
using System.Collections.Generic;
using System.Linq;
using PawnDiary.Capture;
using PawnDiary.Integration;
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
    // Progression groups match synthetic source tokens from the pawn progression scanner.
    // Reflection groups match synthetic day/quadrum/arc reflection source tokens. GravshipJourney
    // matches the one synthetic Odyssey successful-landing Def name. PersonaWeapon matches exact
    // Royalty persona-bond lifecycle page names and has no catch-all.
    // External groups match the eventKey strings other mods submit through the public
    // integration API (PawnDiary.Integration.PawnDiaryApi); adapter mods usually ship them.
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
        Ability,
        Progression,
        Reflection,
        GravshipJourney,
        External,
        PersonaWeapon
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
        // Literal settings overrides for the same combined label/text/instruction. Blank means use the
        // Keyed XML value above. Placeholder arguments follow the matching Keyed fallback at runtime.
        public string labelText;
        public string briefText;
        public string headerText;
        public string fallbackText;
        public string instructionText;
        // When true, each line is "Interaction label: original log text"; false keeps only log text.
        public bool includeInteractionLabel = true;
        // AmbientDayNote only: drop the note if fewer than this many moments happened.
        public int minEventsToWrite = 1;
        // AmbientDayNote only: keep at most this many evidence lines in the raw prompt text.
        public int maxSampleLines = 5;
        // Ordinarily a batch is used only when both interaction pawns own diaries. AmbientDayNote
        // compatibility groups may opt in when exactly one pawn is eligible (for example a colonist
        // talking to a Hospitality guest): that batcher writes only the eligible pawn's solo note.
        // PairEvent does not support this flag. Default false preserves every shipped group's routing.
        public bool allowSingleEligiblePawn = false;
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
        // Literal settings overrides for Immediate mode. Placeholders mirror the keys above:
        // {0}=pawn label, {1}=hediff label.
        public string appearedText;
        public string progressedText;
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

        // Whether the PlayLog capture hook should call RimWorld's grammar renderer to preserve the
        // exact social-log text. Some conversation-framework mods schedule follow-up dialogue while
        // rendering their grammar, so compatibility groups can turn this off and use safe fallback
        // text instead.
        public bool captureRenderedGameText = true;

        // Package IDs that suppress this group while any listed mod is loaded. Use this for
        // compatibility where another mod supplies a richer replacement for a built-in low-value
        // group, while keeping the original group active in ordinary mod lists.
        public List<string> disableWhenPackageIdsLoaded;

        // Stable integration capture-capability ids that suppress this group while any listed richer
        // path reports itself ready. Unlike package gating, this follows actual hook health: if an
        // upstream method rename prevents an adapter from installing its hook, the XML fallback stays
        // active. Empty/absent means no capability-based suppression.
        public List<string> disableWhenCaptureCapabilitiesReady;

        // The inverse gate: when this list is non-empty, the group is active ONLY while at least
        // one listed mod is loaded. This lets compatibility groups for other mods ship inside the
        // core mod (like our DLC string-matchers) and sit fully inert — never recording — for
        // players who don't run the target mod. Empty/absent means "always active" (the default
        // for every ordinary group).
        public List<string> enableWhenPackageIdsLoaded;

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
        // BLUNT by design — a token like "Good" also claims "PawnWithGoodOpinionDied". Prefer the
        // prefix/suffix/segment matchers below for new groups; keep this only for compatibility.
        public List<string> matchTokens;

        // Prefix matchers: a defName that STARTS WITH any prefix (case-insensitive) matches. Optional.
        // Use for defName families that share a leading word, e.g. "Terrible" -> TerribleParty,
        // TerribleFuneral. Narrower than matchTokens because the token must begin the defName.
        public List<string> matchPrefixes;

        // Suffix matchers: a defName that ENDS WITH any suffix (case-insensitive) matches. Optional.
        // Use for defName families that share a trailing word, e.g. "Died" grief thoughts.
        public List<string> matchSuffixes;

        // Segment matchers: a defName matches when any of its CamelCase/underscore/digit SEGMENTS
        // equals any listed segment (case-insensitive). Optional. The most precise of the token-style
        // matchers: "Food" matches "NeedFood" and "AteRawFood" but NOT "Foodstuff" or "Bloodfood".
        // Prefer this over matchTokens whenever a whole word is the intent.
        public List<string> matchSegments;

        // Package IDs: a Def from any listed mod package is claimed by this group. This lets
        // compatibility policy live in XML without referencing another mod's C# types or listing
        // every InteractionDef it adds. Compared case-insensitively against Def.modContentPack.
        public List<string> matchPackageIds;

        // When true this group matches everything in its domain (the catch-all). Give it the
        // highest `order` in its domain so the specific groups get first claim.
        public bool catchAll = false;

        // Classification order within a domain: lower numbers are tested first ("first match
        // wins"). Def load order across files is not guaranteed, so this keeps it deterministic.
        public int order = 0;

        // True if this group claims the given live Def. Package matching is available only for live
        // Defs; saved-event recovery still calls Matches(string) and classifies by defName.
        public bool Matches(Def sourceDef)
        {
            return Matches(sourceDef?.defName, PackageIdFor(sourceDef));
        }

        // True if this group claims the given defName. Check order mirrors the old catalog:
        // catch-all, then exact defNames, then substring tokens. Package IDs are not checked here
        // because callers such as saved-event recovery only have the persisted defName string.
        public bool Matches(string defName)
        {
            return Matches(defName, string.Empty);
        }

        private bool Matches(string defName, string packageId)
        {
            if (catchAll)
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(packageId)
                && InteractionGroups.ContainsDefName(matchPackageIds, packageId))
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

            // Precise token matchers (prefix/suffix/whole-segment) are tested before the blunt
            // substring tokens so a group can claim defName families without misfiring on words
            // that merely contain the same letters. Each delegates to the pure GroupNameMatcher
            // helper so the behavior is identical in the unit-test harness (no DefDatabase needed).
            if (matchPrefixes != null && GroupNameMatcher.MatchesPrefix(defName, matchPrefixes))
            {
                return true;
            }

            if (matchSuffixes != null && GroupNameMatcher.MatchesSuffix(defName, matchSuffixes))
            {
                return true;
            }

            if (matchSegments != null && GroupNameMatcher.MatchesSegment(defName, matchSegments))
            {
                return true;
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

        private static string PackageIdFor(Def sourceDef)
        {
            return sourceDef?.modContentPack?.PackageId ?? string.Empty;
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

        // True when XML says this group should go quiet because a replacement/compatibility package
        // is currently active.
        public bool DisabledByLoadedPackage()
        {
            return InteractionGroups.AnyPackageLoaded(disableWhenPackageIdsLoaded);
        }

        // True when XML says this group needs one of the enableWhenPackageIdsLoaded mods and none
        // of them is in the current mod list — the group is a compatibility pack whose target mod
        // is absent, so it must record nothing. An empty/absent list never blocks the group.
        public bool MissingRequiredPackage()
        {
            return enableWhenPackageIdsLoaded != null
                && enableWhenPackageIdsLoaded.Count > 0
                && !InteractionGroups.AnyPackageLoaded(enableWhenPackageIdsLoaded);
        }

        /// <summary>
        /// True when a richer integration capture path (or an explicit intentional-suppression claim)
        /// currently owns this group's event stream. Capabilities are runtime health, not mod-presence.
        /// </summary>
        public bool DisabledByReadyCaptureCapability()
        {
            return CaptureCapabilities.AnyReady(disableWhenCaptureCapabilitiesReady);
        }

        /// <summary>
        /// Central availability check used at settings, capture, and public-API boundaries. Keep new
        /// runtime gates here so one consumer cannot accidentally classify a dormant compatibility row.
        /// </summary>
        public bool UnavailableForCurrentRuntime()
        {
            return DisabledByLoadedPackage()
                || MissingRequiredPackage()
                || DisabledByReadyCaptureCapability();
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
    internal static class InteractionGroups
    {
        private static List<DiaryInteractionGroupDef> cachedAll;

        // Memoized classification results. The group catalog is immutable after load, so a given Def
        // (or domain+defName string) always classifies to the same group. These collapse the per-call
        // O(groups) `Matches` scan — run on the PlayLog.Add hot path (per logged interaction) and in
        // every periodic capture scan — to one dictionary lookup. Lifetime is tied to `cachedAll`:
        // both are cleared together if the catalog is ever rebuilt (e.g. a dev "reload defs").
        private static readonly Dictionary<Def, DiaryInteractionGroupDef> classifyByDef =
            new Dictionary<Def, DiaryInteractionGroupDef>();
        private static readonly Dictionary<string, DiaryInteractionGroupDef> classifyByDomainName =
            new Dictionary<string, DiaryInteractionGroupDef>();

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
                    // The classification memos are derived from this list; drop them so they
                    // repopulate against the freshly built catalog.
                    classifyByDef.Clear();
                    classifyByDomainName.Clear();
                }

                return cachedAll;
            }
        }

        // First Interaction-domain group that matches the interaction, else the Interaction
        // catch-all ("Other").
        public static DiaryInteractionGroupDef Classify(InteractionDef interactionDef)
        {
            return ClassifyIn(GroupDomain.Interaction, interactionDef);
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

        // First Thought-domain group that matches the live ThoughtDef, else the Thought catch-all.
        // Keep the Def object here (rather than reducing it to defName) so compatibility groups can
        // use matchPackageIds. Saved-event recovery still uses ClassifyDefName below because a save
        // intentionally stores the stable name, not a live Def reference.
        public static DiaryInteractionGroupDef ClassifyThought(ThoughtDef thoughtDef)
        {
            return ClassifyIn(GroupDomain.Thought, thoughtDef);
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
        // Body-part hediffs use a synthetic key (defName plus addedpart/missingpart tokens) so
        // prosthetic installs and missing parts can be XML-routed without adding new C# hooks.
        public static DiaryInteractionGroupDef ClassifyHediff(HediffDef hediffDef)
        {
            if (hediffDef == null)
            {
                return ClassifyHediff((string)null);
            }

            bool isAddedPart = hediffDef.hediffClass != null
                && typeof(Hediff_AddedPart).IsAssignableFrom(hediffDef.hediffClass);
            bool isMissingPart = hediffDef.hediffClass != null
                && typeof(Hediff_MissingPart).IsAssignableFrom(hediffDef.hediffClass);
            string classifierKey = BodyPartEventPolicy.BuildHediffClassifierKey(
                hediffDef.defName,
                isAddedPart,
                isMissingPart,
                isAddedPart && hediffDef.organicAddedBodypart);
            return ClassifyHediff(classifierKey);
        }

        // First Hediff-domain group that matches an already-built health classifier key.
        public static DiaryInteractionGroupDef ClassifyHediff(string hediffClassifierKey)
        {
            return ClassifyIn(GroupDomain.Hediff, hediffClassifierKey);
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

        // First Progression-domain group that matches the synthetic progression source token, else the
        // Progression catch-all. The scanner emits tokens like SkillMilestone and XenotypeChanged.
        public static DiaryInteractionGroupDef ClassifyProgression(string progressionDefName)
        {
            return ClassifyIn(GroupDomain.Progression, progressionDefName);
        }

        // Odyssey landing has an exact synthetic Def name and no catch-all. The shipped XML row is
        // package-gated, so the classifier remains inert and the settings row stays hidden without DLC.
        public static DiaryInteractionGroupDef ClassifyGravshipJourney(string journeyDefName)
        {
            return ClassifyRequiredMatch(GroupDomain.GravshipJourney, journeyDefName);
        }

        // Royalty persona lifecycle pages use exact synthetic Def names and no catch-all. The one
        // shipped row is package-gated, so it is inert and absent from settings without Royalty.
        public static DiaryInteractionGroupDef ClassifyPersonaWeapon(string eventDefName)
        {
            return ClassifyRequiredMatch(GroupDomain.PersonaWeapon, eventDefName);
        }

        // First External-domain group that explicitly matches an integration-API eventKey. Like
        // Romance, live capture must NOT fall back to a catch-all here: only eventKeys some XML
        // group claims may create diary entries, so an unclaimed submission from another mod is
        // silently inert. Adapter mods ship their own External groups to claim their keys.
        public static DiaryInteractionGroupDef ClassifyExternal(string eventKey)
        {
            return ClassifyRequiredMatch(GroupDomain.External, eventKey);
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
            // Touch All first so a catalog rebuild clears the memo before we read it.
            List<DiaryInteractionGroupDef> all = All;
            string cacheKey = ((int)domain) + "|" + (defName ?? string.Empty);
            DiaryInteractionGroupDef cached;
            if (classifyByDomainName.TryGetValue(cacheKey, out cached))
            {
                return cached;
            }

            DiaryInteractionGroupDef fallback = null;
            DiaryInteractionGroupDef result = null;
            for (int i = 0; i < all.Count; i++)
            {
                DiaryInteractionGroupDef group = all[i];
                if (group.domain != domain)
                {
                    continue;
                }

                if (group.Matches(defName))
                {
                    result = group;
                    break;
                }

                fallback = group;
            }

            result = result ?? fallback;
            classifyByDomainName[cacheKey] = result;
            return result;
        }

        private static DiaryInteractionGroupDef ClassifyIn(GroupDomain domain, Def sourceDef)
        {
            // Touch All first so a catalog rebuild clears the memo before we read it.
            List<DiaryInteractionGroupDef> all = All;

            // A Def is only ever classified within its own domain, so the Def reference alone is a
            // sufficient cache key. A null Def falls through to the defName-string path (also memoized).
            if (sourceDef == null)
            {
                return ClassifyIn(domain, (string)null);
            }

            DiaryInteractionGroupDef cached;
            if (classifyByDef.TryGetValue(sourceDef, out cached))
            {
                return cached;
            }

            DiaryInteractionGroupDef fallback = null;
            DiaryInteractionGroupDef result = null;
            for (int i = 0; i < all.Count; i++)
            {
                DiaryInteractionGroupDef group = all[i];
                if (group.domain != domain)
                {
                    continue;
                }

                if (group.Matches(sourceDef))
                {
                    result = group;
                    break;
                }

                fallback = group;
            }

            result = result ?? fallback;
            classifyByDef[sourceDef] = result;
            return result;
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

        // Progression-domain instruction. The scanner has already picked the group from the synthetic
        // progression source token, so this mirrors work-domain instruction resolution.
        public static string InstructionForProgression(DiaryInteractionGroupDef group)
        {
            return InstructionForGroup(group);
        }

        // GravshipJourney-domain instruction. The signal already resolved the exact landing group.
        public static string InstructionForGravshipJourney(DiaryInteractionGroupDef group)
        {
            return InstructionForGroup(group);
        }

        // PersonaWeapon-domain instruction. The lifecycle adapter already resolved the exact group.
        public static string InstructionForPersonaWeapon(DiaryInteractionGroupDef group)
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

        internal static bool AnyPackageLoaded(List<string> packageIds)
        {
            if (packageIds == null || packageIds.Count == 0)
            {
                return false;
            }

            List<ModContentPack> loadedMods = LoadedModManager.RunningModsListForReading;
            if (loadedMods == null || loadedMods.Count == 0)
            {
                return false;
            }

            for (int i = 0; i < loadedMods.Count; i++)
            {
                string loadedPackageId = loadedMods[i]?.PackageId;
                if (ContainsDefName(packageIds, loadedPackageId))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
