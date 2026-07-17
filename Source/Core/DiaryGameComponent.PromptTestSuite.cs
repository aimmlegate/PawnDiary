// Dev-only "prompt test suite": a data-driven catalog of synthetic diary-event fixtures. The Debug
// Actions event panel can batch-generate selected fixtures, deleting any prior test entries first,
// then routing synthetic events through the normal generation queue. With prompt test mode ON,
// QueuePrompt captures the assembled prompt and stamps the role prompt_only (no LLM call), so the
// selected categories yield prompt-only cards you can inspect in the Diary tab. A separate clear
// action deletes every test entry.
//
// Single source of truth: the debug panel iterates `SuiteEntries` below — the SAME registry the
// builder reads. Adding a future category means appending one entry here (plus its keyed strings); it
// then auto-appears in the panel with no UI-code change and no copy-paste.
//
// Pair fixtures cross-reference the selected pawn (initiator) and a second colonist (recipient), so a
// pair category also produces a recipient POV prompt-only card in that other colonist's Diary tab.
//
// New to C#/RimWorld? This is a partial class of DiaryGameComponent (see AGENTS.md). It reuses the
// private event factories (AddSoloEvent / AddPairwiseEvent), the private queue dispatcher
// (EnsureGenerationQueued), and the private event stores (diaryEvents / eventsById / diaries) because
// partial classes share private access.
using System;
using System.Collections.Generic;
using PawnDiary.Capture;
using RimWorld;
using Verse;

namespace PawnDiary
{
    public partial class DiaryGameComponent
    {
        // Stable marker stamped into every suite event's gameContext so ClearPromptSuiteForDev can find
        // and remove them. Purely a tag; no behavior reads it.
        private const string DevPromptSuiteMarker = "dev_prompt_suite=true";
        private const string DevPromptSuiteMarkerKey = "dev_prompt_suite=";
        private const string DevPromptSuiteInstructionKey = "PawnDiary.Dev.PromptSuite.Instruction";
        private const string DevPromptSuiteReasonKey = "PawnDiary.Dev.PromptSuite.Reason";

        /// <summary>
        /// One selectable prompt-test fixture. The dropdown label comes from <see cref="labelKey"/>;
        /// the remaining fields drive the synthetic event's defName, gameContext markers, and POV texts.
        /// This record is the single source both the UI dropdown and the builder consume.
        /// </summary>
        internal sealed class DevPromptSuiteEntry
        {
            public string id;
            public string labelKey;
            internal DevPromptSuiteFixtureShape shape;
            public string eventDefName;
            public string markers; // gameContext driving tokens, WITHOUT label/reason/dev_prompt_suite
            public string markersKey; // optional localized gameContext used by DLC prompt previews
            public string reasonKey; // optional keyed reason appended to context (null for most)
            public string initiatorTextKey; // pair only
            public string recipientTextKey; // pair only
            public string textKey; // solo only

            /// <summary>True when this fixture needs a second eligible colonist.</summary>
            public bool pair
            {
                get { return shape == DevPromptSuiteFixtureShape.Pair; }
            }
        }

        // The neutral arrival/death fixtures queue the `neutral` POV instead of a first-person POV.
        // Keep that difference explicit here so the registry remains readable to people adding tests.
        internal enum DevPromptSuiteFixtureShape
        {
            Solo,
            Pair,
            ArrivalDescription,
            DeathDescription
        }

        // The catalog — the single source of truth. Append a row here (plus its keyed strings in
        // PawnDiary.xml) to add a test category; it then appears in the dropdown automatically. Markers
        // mirror the real capture-time gameContext for each domain so template selection routes each
        // fixture to the same prompt shape as a live event of that kind.
        private static readonly DevPromptSuiteEntry[] SuiteEntries =
        {
            Pair("Insult", "PawnDiary.Dev.PromptSuite.Insult.Label", "Insult",
                "def=Insult; worker=Interaction_Insult", null,
                "PawnDiary.Dev.PromptSuite.Insult.Initiator", "PawnDiary.Dev.PromptSuite.Insult.Recipient"),
            Pair("SocialFight", "PawnDiary.Dev.PromptSuite.SocialFight.Label", "SocialFighting",
                "mental_state=SocialFighting", DevPromptSuiteReasonKey,
                "PawnDiary.Dev.PromptSuite.SocialFight.Initiator", "PawnDiary.Dev.PromptSuite.SocialFight.Recipient"),
            Pair("Romance", "PawnDiary.Dev.PromptSuite.Romance.Label", "Spouse",
                "romance=Spouse; kind=married", null,
                "PawnDiary.Dev.PromptSuite.Romance.Initiator", "PawnDiary.Dev.PromptSuite.Romance.Recipient"),
            Solo("MentalBreak", "PawnDiary.Dev.PromptSuite.MentalBreak.Label", "Berserk",
                "mental_state=Berserk", DevPromptSuiteReasonKey, "PawnDiary.Dev.PromptSuite.MentalBreak.Text"),
            Arrival("Arrival", "PawnDiary.Dev.PromptSuite.Arrival.Label", ArrivalEventData.DefNameToken,
                "PawnDiary.Dev.PromptSuite.Arrival.Text"),
            Death("Death", "PawnDiary.Dev.PromptSuite.Death.Label", DeathEventData.DefNameToken,
                "PawnDiary.Dev.PromptSuite.Death.Text"),
            Solo("Hediff", "PawnDiary.Dev.PromptSuite.Hediff.Label", "Flu",
                "hediff=Flu; source=add; group=hediffMajorHealth; mode=Immediate; severity=0.45; stage=1",
                null, "PawnDiary.Dev.PromptSuite.Hediff.Text"),
            Solo("Inspiration", "PawnDiary.Dev.PromptSuite.Inspiration.Label", "Inspired_Recruitment",
                "inspiration=Inspired_Recruitment; duration_days=8", null, "PawnDiary.Dev.PromptSuite.Inspiration.Text"),
            Solo("Work", "PawnDiary.Dev.PromptSuite.Work.Label", PawnDiary.Capture.WorkEventData.PassionDefName,
                "work=Research; work_giver=DoResearch; mood_impact=positive; passion=true; low_skill=false; dumb_or_cleaning=false; dark_study=false",
                null, "PawnDiary.Dev.PromptSuite.Work.Text"),
            Solo("Thought", "PawnDiary.Dev.PromptSuite.Thought.Label", "AteWithoutTable",
                "thought=AteWithoutTable; mood_impact=negative; mood_offset=-5; duration_days=1",
                null, "PawnDiary.Dev.PromptSuite.Thought.Text"),
            Solo("ThoughtProgression", "PawnDiary.Dev.PromptSuite.ThoughtProgression.Label", "NeedFood",
                "thought=NeedFood; thought_progression=need_food; label=Starving; stage_index=3; severity=3; mood_impact=negative; mood_offset=-18.0",
                null, "PawnDiary.Dev.PromptSuite.ThoughtProgression.Text"),
            Solo("MoodEvent", "PawnDiary.Dev.PromptSuite.MoodEvent.Label", "HeatWave",
                "mood_event=HeatWave; mood_impact=negative", null, "PawnDiary.Dev.PromptSuite.MoodEvent.Text"),
            Solo("Tale", "PawnDiary.Dev.PromptSuite.Tale.Label", "FinishedResearchProject",
                "tale=FinishedResearchProject; taleClass=Tale_SinglePawn", null, "PawnDiary.Dev.PromptSuite.Tale.Text"),
            Solo("Raid", "PawnDiary.Dev.PromptSuite.Raid.Label", "RaidEnemy",
                "raid=RaidEnemy; label=Raid; faction=Pirate; points=500; arrival_mode=EdgeWalkIn; strategy=ImmediateAttack",
                null, "PawnDiary.Dev.PromptSuite.Raid.Text"),
            Solo("Quest", "PawnDiary.Dev.PromptSuite.Quest.Label", "OpportunitySite_ItemStash",
                "quest=OpportunitySite_ItemStash; signal=accepted; label=The Stash; faction=OutlanderCivil; rewards=medicine x20; quest_label=The Stash; quest_signal=accepted; quest_faction=OutlanderCivil; quest_rewards=medicine x20",
                null, "PawnDiary.Dev.PromptSuite.Quest.Text"),
            Solo("Ritual", "PawnDiary.Dev.PromptSuite.Ritual.Label", "Funeral",
                "ritual=Funeral; ritual_title=Funeral; ritual_behavior=RitualBehaviorWorker_Funeral; ritual_perspective=participant; ritual_role=participant; royal_title=none; ideological_role=none; outcome=finished; quality=good",
                null, "PawnDiary.Dev.PromptSuite.Ritual.Text"),
            Solo("Ability", "PawnDiary.Dev.PromptSuite.Ability.Label", "WordOfJoy",
                "ability=WordOfJoy; ability_label=Word of joy; ability_category=Psycast; ability_cooldown_ticks=60000; ability_record_chance=1; ability_target=Self",
                null, "PawnDiary.Dev.PromptSuite.Ability.Text"),
            Solo("DayReflection", "PawnDiary.Dev.PromptSuite.DayReflection.Label", "PawnDiary_DayReflection",
                "day_reflection=true; day=42; highlights=3; candidates=6; filler_moments=2; signals=health,work,social",
                null, "PawnDiary.Dev.PromptSuite.DayReflection.Text"),
            Solo("QuadrumReflection", "PawnDiary.Dev.PromptSuite.QuadrumReflection.Label", "QuadrumReflection",
                "day_reflection=true; quadrum_reflection=true; day=44; quadrum=2; quadrum_start_day=30; quadrum_end_day=44; quadrum_dates=1st of Aprimay - 15th of Aprimay; due_day=42; highlights=6; candidates=10; important_entries=10; filler_moments=0; signals=event:raid,event:progression",
                null, "PawnDiary.Dev.PromptSuite.QuadrumReflection.Text"),
            Solo("ProgressionSkillPassion", "PawnDiary.Dev.PromptSuite.ProgressionSkillPassion.Label", ProgressionEventData.SkillMilestoneDefName,
                "progression=SkillMilestone; progression_kind=skill; previous_value=8; new_value=12; skill=Construction; skill_level=12; previous_skill_milestone=8; passion=major",
                null, "PawnDiary.Dev.PromptSuite.ProgressionSkillPassion.Text"),
            Solo("ProgressionPsylink", "PawnDiary.Dev.PromptSuite.ProgressionPsylink.Label", ProgressionEventData.PsylinkLevelDefName,
                "progression=PsylinkLevel; progression_kind=psylink; previous_value=2; new_value=3; psylink_level=3; previous_psylink_level=2",
                null, "PawnDiary.Dev.PromptSuite.ProgressionPsylink.Text"),
            Solo("ProgressionXenotypeSanguophage", "PawnDiary.Dev.PromptSuite.ProgressionXenotypeSanguophage.Label", ProgressionEventData.XenotypeChangedDefName,
                "progression=XenotypeChanged; progression_kind=xenotype; previous_value=Baseliner; new_value=Sanguophage; previous_xenotype=Baseliner; xenotype=Sanguophage; xenotype_def=Sanguophage; major_xenotype=true",
                null, "PawnDiary.Dev.PromptSuite.ProgressionXenotypeSanguophage.Text"),
            Solo("ProgressionRoyalTitle", "PawnDiary.Dev.PromptSuite.ProgressionRoyalTitle.Label", ProgressionEventData.RoyalTitleChangedDefName,
                "progression=RoyalTitleChanged; progression_kind=royal_title; previous_value=Yeoman; new_value=Knight; previous_title=Yeoman; title=Knight; title_def=Knight",
                null, "PawnDiary.Dev.PromptSuite.ProgressionRoyalTitle.Text"),
            Solo("ProgressionTraitGained", "PawnDiary.Dev.PromptSuite.ProgressionTraitGained.Label", ProgressionEventData.TraitGainedDefName,
                "progression=TraitGained; progression_kind=trait; new_value=Nervous; trait=Nervous; trait_def=Nerves; trait_description=Some people are just high-strung, prone to worry and quicker to break under strain. This one is one of them.",
                null, "PawnDiary.Dev.PromptSuite.ProgressionTraitGained.Text"),
            PairWithLocalizedMarkers(
                "BiotechGrowth",
                "PawnDiary.Dev.PromptSuite.BiotechGrowth.Label",
                GrowthMomentEventData.DefName,
                "PawnDiary.Dev.PromptSuite.BiotechGrowth.Markers",
                "PawnDiary.Dev.PromptSuite.BiotechGrowth.Initiator",
                "PawnDiary.Dev.PromptSuite.BiotechGrowth.Recipient"),
            PairWithLocalizedMarkers(
                "BiotechBirth",
                "PawnDiary.Dev.PromptSuite.BiotechBirth.Label",
                FamilyBirthEventData.DefName,
                "PawnDiary.Dev.PromptSuite.BiotechBirth.Markers",
                "PawnDiary.Dev.PromptSuite.BiotechBirth.Initiator",
                "PawnDiary.Dev.PromptSuite.BiotechBirth.Recipient"),
            PairWithLocalizedMarkers(
                "OdysseyLanding",
                "PawnDiary.Dev.PromptSuite.OdysseyLanding.Label",
                OdysseyEventDefNames.Landing,
                "PawnDiary.Dev.PromptSuite.OdysseyLanding.Markers",
                "PawnDiary.Dev.PromptSuite.OdysseyLanding.Initiator",
                "PawnDiary.Dev.PromptSuite.OdysseyLanding.Recipient"),
            Solo("ArcReflectionForced", "PawnDiary.Dev.PromptSuite.ArcReflectionForced.Label", ArcReflectionEventData.DefNameToken,
                "arc_reflection=true; arc_year=5504; forced=true; selected_memories=6; candidate_memories=18; entries_this_year=0",
                null, "PawnDiary.Dev.PromptSuite.ArcReflectionForced.Text"),
            Solo("ArcReflectionMajorEvent", "PawnDiary.Dev.PromptSuite.ArcReflectionMajorEvent.Label", ArcReflectionEventData.DefNameToken,
                "arc_reflection=true; arc_year=5504; forced=false; selected_memories=5; candidate_memories=14; entries_this_year=0",
                null, "PawnDiary.Dev.PromptSuite.ArcReflectionMajorEvent.Text"),
            Solo("ArcReflectionCooldownBlocked", "PawnDiary.Dev.PromptSuite.ArcReflectionCooldownBlocked.Label", ArcReflectionEventData.DefNameToken,
                "arc_reflection=true; arc_year=5504; forced=false; selected_memories=4; candidate_memories=12; entries_this_year=1",
                null, "PawnDiary.Dev.PromptSuite.ArcReflectionCooldownBlocked.Text"),
        };

        /// <summary>The full catalog in display order. The debug panel iterates this — never hardcode the
        /// category list in UI code, or the two will drift.</summary>
        internal static IReadOnlyList<DevPromptSuiteEntry> AllSuiteEntries => SuiteEntries;

        private static DevPromptSuiteEntry Pair(string id, string labelKey, string eventDefName,
            string markers, string reasonKey, string initiatorTextKey, string recipientTextKey)
        {
            return new DevPromptSuiteEntry
            {
                id = id,
                labelKey = labelKey,
                shape = DevPromptSuiteFixtureShape.Pair,
                eventDefName = eventDefName,
                markers = markers,
                reasonKey = reasonKey,
                initiatorTextKey = initiatorTextKey,
                recipientTextKey = recipientTextKey
            };
        }

        private static DevPromptSuiteEntry Solo(string id, string labelKey, string eventDefName,
            string markers, string reasonKey, string textKey)
        {
            return new DevPromptSuiteEntry
            {
                id = id,
                labelKey = labelKey,
                shape = DevPromptSuiteFixtureShape.Solo,
                eventDefName = eventDefName,
                markers = markers,
                reasonKey = reasonKey,
                textKey = textKey
            };
        }

        /// <summary>
        /// Defines a pair fixture whose model-facing context values come from a Keyed translation.
        /// Stable schema names remain English, while descriptive values follow the active language.
        /// </summary>
        private static DevPromptSuiteEntry PairWithLocalizedMarkers(
            string id,
            string labelKey,
            string eventDefName,
            string markersKey,
            string initiatorTextKey,
            string recipientTextKey)
        {
            return new DevPromptSuiteEntry
            {
                id = id,
                labelKey = labelKey,
                shape = DevPromptSuiteFixtureShape.Pair,
                eventDefName = eventDefName,
                markersKey = markersKey,
                initiatorTextKey = initiatorTextKey,
                recipientTextKey = recipientTextKey
            };
        }

        private static DevPromptSuiteEntry Arrival(string id, string labelKey, string eventDefName, string textKey)
        {
            return new DevPromptSuiteEntry
            {
                id = id,
                labelKey = labelKey,
                shape = DevPromptSuiteFixtureShape.ArrivalDescription,
                eventDefName = eventDefName,
                textKey = textKey
            };
        }

        private static DevPromptSuiteEntry Death(string id, string labelKey, string eventDefName, string textKey)
        {
            return new DevPromptSuiteEntry
            {
                id = id,
                labelKey = labelKey,
                shape = DevPromptSuiteFixtureShape.DeathDescription,
                eventDefName = eventDefName,
                textKey = textKey
            };
        }

        /// <summary>
        /// Dev-only: the catalog entries buildable for this pawn right now — solo entries always, pair
        /// entries only when a second colonist is available. The debug panel lists exactly this.
        /// </summary>
        internal IReadOnlyList<DevPromptSuiteEntry> AvailableSuiteEntriesForDev(Pawn pawn)
        {
            List<DevPromptSuiteEntry> result = new List<DevPromptSuiteEntry>();
            if (!Prefs.DevMode || pawn == null)
            {
                return result;
            }

            bool hasPartner = ResolvePairPartner(pawn, null) != null;
            for (int i = 0; i < SuiteEntries.Length; i++)
            {
                DevPromptSuiteEntry entry = SuiteEntries[i];
                if (entry != null && (!entry.pair || hasPartner))
                {
                    result.Add(entry);
                }
            }

            return result;
        }

        /// <summary>
        /// Dev-only: deletes every prompt-test entry (any event tagged <c>dev_prompt_suite</c>) from
        /// the master event list, the O(1) lookup index, and every pawn's diary ref list. Returns the
        /// number removed. Safe to call when there are none.
        /// </summary>
        internal int ClearPromptSuiteForDev()
        {
            if (!Prefs.DevMode || events.Count == 0)
            {
                return 0;
            }

            HashSet<string> toRemove = new HashSet<string>();
            IReadOnlyList<DiaryEvent> allEvents = events.AllEvents;
            for (int i = 0; i < allEvents.Count; i++)
            {
                DiaryEvent diaryEvent = allEvents[i];
                if (diaryEvent != null && DiaryContextFields.HasMarker(diaryEvent.gameContext, DevPromptSuiteMarkerKey))
                {
                    toRemove.Add(diaryEvent.eventId);
                }
            }

            if (toRemove.Count == 0)
            {
                return 0;
            }

            // Drop from the master list + lookup index first, then scrub refs from every pawn diary so
            // no saved record points at a removed event. Also drop any compacted archive rows for these
            // events: a prompt-suite entry that was generated and later aged into the archive must not
            // survive the reset as an orphaned page.
            events.RemoveEvents(toRemove);
            archive.RemoveForEventIds(toRemove);

            if (diaries != null)
            {
                for (int i = 0; i < diaries.Count; i++)
                {
                    PawnDiaryRecord diary = diaries[i];
                    if (diary != null && diary.eventIds != null)
                    {
                        diary.eventIds.RemoveAll(id => toRemove.Contains(id));
                    }
                }
            }

            DiaryStateVersion.Bump();
            return toRemove.Count;
        }

        /// <summary>
        /// Dev-only: replaces any current test entry with exactly ONE synthetic event for the given
        /// catalog entry, then routes it through the generation queue so its prompt is captured as a
        /// prompt_only card. Returns true on success; false outside dev mode, when prompt test mode is
        /// off, when generation is disabled for the pawn, or when a pair entry has no partner. The UI
        /// handler enables prompt test mode before calling.
        /// </summary>
        internal bool ShowPromptSuiteEntryForDev(Pawn pawn, DevPromptSuiteEntry entry)
        {
            if (!CanUsePromptSuiteForDev(pawn) || entry == null)
            {
                return false;
            }

            // Replace semantics: clear previous test entries first so only one is live at a time.
            ClearPromptSuiteForDev();
            return AppendPromptSuiteEntryForDev(pawn, entry);
        }

        /// <summary>
        /// Dev-only batch helper for the RimWorld debug-action panel: replaces current test entries
        /// with every requested fixture that is buildable for the selected pawn. The caller supplies
        /// the already-filtered list so the panel can support checkboxes as well as "all".
        /// </summary>
        internal int ShowPromptSuiteEntriesForDev(Pawn pawn, IEnumerable<DevPromptSuiteEntry> entries)
        {
            if (!CanUsePromptSuiteForDev(pawn) || entries == null)
            {
                return 0;
            }

            ClearPromptSuiteForDev();

            int shown = 0;
            foreach (DevPromptSuiteEntry entry in entries)
            {
                if (AppendPromptSuiteEntryForDev(pawn, entry))
                {
                    shown++;
                }
            }

            return shown;
        }

        private bool CanUsePromptSuiteForDev(Pawn pawn)
        {
            return Prefs.DevMode && PromptTestModeEnabled() && IsDiaryEligible(pawn)
                && DiaryGenerationEnabledFor(pawn);
        }

        private bool AppendPromptSuiteEntryForDev(Pawn pawn, DevPromptSuiteEntry entry)
        {
            if (!CanUsePromptSuiteForDev(pawn) || entry == null)
            {
                return false;
            }

            Pawn partner = null;
            if (entry.pair)
            {
                partner = ResolvePairPartner(pawn, null);
                if (partner == null)
                {
                    return false;
                }
            }

            DiaryEvent diaryEvent = BuildSuiteEvent(entry, pawn, partner);
            if (diaryEvent == null)
            {
                return false;
            }

            // Capture the prompt immediately. In prompt test mode QueuePrompt is synchronous (it stamps
            // prompt_only and returns), so both POVs of a pair event are captured on this call.
            Dictionary<string, Pawn> livePawnsById = SnapshotLivePawnsByLoadId();
            if (diaryEvent.HasArrivalDescription() || diaryEvent.HasDeathDescription())
            {
                EnsureGenerationQueued(diaryEvent, DiaryEvent.NeutralRole, null, livePawnsById);
                return true;
            }

            EnsureGenerationQueued(diaryEvent, DiaryEvent.InitiatorRole, null, livePawnsById);
            if (!diaryEvent.solo)
            {
                EnsureGenerationQueued(diaryEvent, DiaryEvent.RecipientRole, null, livePawnsById);
            }
            return true;
        }

        /// <summary>
        /// Builds and registers one synthetic event from a catalog entry. Assembles gameContext from
        /// the entry's markers plus the translated label/optional reason and the dev_prompt_suite tag,
        /// then neutralizes captured decoration so the prompt renders as plain prose (no atmosphere,
        /// staggered speech, or category page wash) — the user wants test prompts shown undecorated.
        /// </summary>
        private DiaryEvent BuildSuiteEvent(DevPromptSuiteEntry entry, Pawn pawn, Pawn partner)
        {
            string label = SuiteLabel(entry.labelKey);
            string instruction = SuiteInstruction();
            string context = SuiteContext(entry, pawn, partner, label);

            DiaryEvent diaryEvent;
            if (entry.pair)
            {
                string initiatorText = SuiteText(entry.initiatorTextKey, partner.LabelShortCap);
                string recipientText = SuiteText(entry.recipientTextKey, pawn.LabelShortCap);
                diaryEvent = AddPairwiseEvent(pawn, partner, entry.eventDefName, label, initiatorText, recipientText, instruction, context);
            }
            else
            {
                string text = SuiteText(entry.textKey, pawn.LabelShortCap);
                diaryEvent = AddSoloEvent(pawn, null, entry.eventDefName, label, text, instruction, context);
            }

            if (diaryEvent != null)
            {
                // "No decoration": clear captured text-decoration/staggered state and use a neutral
                // color cue so no category-tinted page wash or fractured/staggered text effect is
                // applied to the captured prompt text.
                diaryEvent.colorCue = string.Empty;
                diaryEvent.initiatorStaggeredIntensity = 0;
                diaryEvent.recipientStaggeredIntensity = 0;
                diaryEvent.initiatorTextDecorationFacts = string.Empty;
                diaryEvent.recipientTextDecorationFacts = string.Empty;
            }

            return diaryEvent;
        }

        private static string SuiteContext(DevPromptSuiteEntry entry, Pawn pawn, Pawn partner, string label)
        {
            if (entry == null)
            {
                return DevPromptSuiteMarker;
            }

            if (entry.shape == DevPromptSuiteFixtureShape.ArrivalDescription)
            {
                string pawnLabel = DiaryLineCleaner.CleanLine(pawn?.LabelShortCap);
                string pawnId = pawn?.GetUniqueLoadID() ?? string.Empty;
                string arrivalFacts = "arrival_source=game_start; arrival_context=synthetic_dev_test";
                return ArrivalEventData.BuildGameContext(pawnLabel, pawnId, arrivalFacts)
                    + "; label=" + label
                    + "; " + DevPromptSuiteMarker;
            }

            if (entry.shape == DevPromptSuiteFixtureShape.DeathDescription)
            {
                string pawnLabel = DiaryLineCleaner.CleanLine(pawn?.LabelShortCap);
                string pawnId = pawn?.GetUniqueLoadID() ?? string.Empty;
                string deathFacts = "death_cause=synthetic_dev_test; killer=none; weapon=none";
                return DeathEventData.BuildFallbackGameContext(
                    entry.eventDefName,
                    DiaryLineCleaner.CleanLine(label),
                    pawnLabel,
                    pawnId,
                    DiaryEvent.InitiatorRole,
                    deathFacts)
                    + "; " + DevPromptSuiteMarker;
            }

            string markers = string.IsNullOrWhiteSpace(entry.markersKey)
                ? entry.markers
                : SuiteMarkers(entry.markersKey, pawn, partner);
            return markers
                + "; label=" + label
                + (string.IsNullOrEmpty(entry.reasonKey) ? string.Empty : "; reason=" + SuiteReason(entry.reasonKey))
                + "; " + DevPromptSuiteMarker;
        }

        /// <summary>
        /// Picks the second colonist for pair categories: an explicit override, else the first other
        /// eligible free colonist. Returns null when no partner is available (pair categories skipped).
        /// </summary>
        private Pawn ResolvePairPartner(Pawn pawn, Pawn otherPawn)
        {
            if (otherPawn != null && otherPawn != pawn && IsDiaryEligible(otherPawn))
            {
                return otherPawn;
            }

            List<Pawn> colonists = SnapshotFreeColonists();
            for (int i = 0; i < colonists.Count; i++)
            {
                Pawn candidate = colonists[i];
                if (candidate != null && candidate != pawn && IsDiaryEligible(candidate))
                {
                    return candidate;
                }
            }

            return null;
        }

        // ---- shared localization helpers ----
        // Label/text/instruction/reason reach the LLM prompt as evidence, so per AGENTS.md §12 they
        // are keyed (never hardcoded English). Resolve() returns the plain string from the TaggedString.

        private static string SuiteInstruction()
        {
            return SuiteLabel(DevPromptSuiteInstructionKey);
        }

        private static string SuiteReason(string key)
        {
            return SuiteLabel(key);
        }

        private static string SuiteMarkers(string key, Pawn pawn, Pawn partner)
        {
            string pawnName = DiaryLineCleaner.CleanLine(pawn?.LabelShortCap);
            string partnerName = DiaryLineCleaner.CleanLine(partner?.LabelShortCap);
            return key.Translate(pawnName, partnerName).Resolve();
        }

        private static string SuiteLabel(string key)
        {
            return key.Translate().Resolve();
        }

        private static string SuiteText(string key, string name)
        {
            return key.Translate(name).Resolve();
        }
    }
}
