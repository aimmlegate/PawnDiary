// Dev-only "prompt test suite": a data-driven catalog of synthetic diary-event fixtures. The Diary
// tab's "Prompt suite" button opens a dropdown of these fixtures; selecting one deletes any prior
// test entries, builds exactly ONE synthetic event for the chosen category, and routes it through the
// normal generation queue. With prompt test mode ON, QueuePrompt captures the assembled prompt and
// stamps the role prompt_only (no LLM call), so the selected category yields one prompt-only card you
// can inspect in the Diary tab. A separate "Clear test prompts" button deletes every test entry.
//
// Single source of truth: the dropdown iterates `SuiteEntries` below — the SAME registry the builder
// reads. Adding a future category means appending one entry here (plus its keyed strings); it then
// auto-appears in the dropdown with no UI-code change and no copy-paste.
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
        public sealed class DevPromptSuiteEntry
        {
            public string id;
            public string labelKey;
            public bool pair;
            public string eventDefName;
            public string markers; // gameContext driving tokens, WITHOUT label/reason/dev_prompt_suite
            public string reasonKey; // optional keyed reason appended to context (null for most)
            public string initiatorTextKey; // pair only
            public string recipientTextKey; // pair only
            public string textKey; // solo only
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
            Solo("MoodEvent", "PawnDiary.Dev.PromptSuite.MoodEvent.Label", "HeatWave",
                "mood_event=HeatWave; mood_impact=negative", null, "PawnDiary.Dev.PromptSuite.MoodEvent.Text"),
            Solo("Tale", "PawnDiary.Dev.PromptSuite.Tale.Label", "FinishedResearchProject",
                "tale=FinishedResearchProject; taleClass=Tale_SinglePawn", null, "PawnDiary.Dev.PromptSuite.Tale.Text"),
            Solo("DayReflection", "PawnDiary.Dev.PromptSuite.DayReflection.Label", "PawnDiary_DayReflection",
                "day_reflection=true; day=42; highlights=3; candidates=6; filler_moments=2; signals=health,work,social",
                null, "PawnDiary.Dev.PromptSuite.DayReflection.Text"),
        };

        /// <summary>The full catalog in display order. The dropdown iterates this — never hardcode the
        /// category list in UI code, or the two will drift.</summary>
        public static IReadOnlyList<DevPromptSuiteEntry> AllSuiteEntries => SuiteEntries;

        private static DevPromptSuiteEntry Pair(string id, string labelKey, string eventDefName,
            string markers, string reasonKey, string initiatorTextKey, string recipientTextKey)
        {
            return new DevPromptSuiteEntry
            {
                id = id,
                labelKey = labelKey,
                pair = true,
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
                pair = false,
                eventDefName = eventDefName,
                markers = markers,
                reasonKey = reasonKey,
                textKey = textKey
            };
        }

        /// <summary>
        /// Dev-only: the catalog entries buildable for this pawn right now — solo entries always, pair
        /// entries only when a second colonist is available. The dropdown lists exactly this.
        /// </summary>
        public IReadOnlyList<DevPromptSuiteEntry> AvailableSuiteEntriesForDev(Pawn pawn)
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
        public int ClearPromptSuiteForDev()
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
            // no saved record points at a removed event.
            events.RemoveEvents(toRemove);

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
        public bool ShowPromptSuiteEntryForDev(Pawn pawn, DevPromptSuiteEntry entry)
        {
            if (!Prefs.DevMode || !PromptTestModeEnabled() || !IsDiaryEligible(pawn)
                || !DiaryGenerationEnabledFor(pawn) || entry == null)
            {
                return false;
            }

            // Replace semantics: clear previous test entries first so only one is live at a time.
            ClearPromptSuiteForDev();

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
            string context = entry.markers
                + "; label=" + label
                + (string.IsNullOrEmpty(entry.reasonKey) ? string.Empty : "; reason=" + SuiteReason(entry.reasonKey))
                + "; " + DevPromptSuiteMarker;

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
