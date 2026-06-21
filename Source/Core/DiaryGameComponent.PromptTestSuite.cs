// Dev-only "prompt test suite": seeds one synthetic diary event per major category and routes each
// through the normal generation queue. With prompt test mode ON, QueuePrompt captures the assembled
// prompt and stamps the role prompt_only (no LLM call), so every category yields a prompt-only card
// you can inspect in the Diary tab. This is the cheapest way to exercise every prompt shape.
//
// What it covers (one card per category, per POV):
//   Pair  : Insult (PairCombat), Social fight (PairCombat via MentalState), Romance (PairImportant)
//   Solo  : Mental break (SoloImportant), Hediff/Inspiration/Work/Thought/Mood event (SoloInternalState),
//           Tale (SoloDefault), Day reflection (SoloDayReflection)
//
// Death and Arrival shapes are INTENTIONALLY NOT synthesized. Both are read by ComputeDiaryBounds as
// the pawn's diary boundaries (first arrival / final death), so inserting fake ones on a real pawn
// would hide that pawn's real pages. Test those two shapes through real gameplay hooks instead.
//
// Pair events cross-reference the selected pawn (initiator) and a second colonist (recipient), so the
// recipient POV prompt-only cards appear in that other colonist's Diary tab, not the selected one.
//
// New to C#/RimWorld? This is a partial class of DiaryGameComponent (see AGENTS.md). It reuses the
// private event factories (AddSoloEvent / AddPairwiseEvent) and the private queue dispatcher
// (EnsureGenerationQueued) because partial classes share private access.
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace PawnDiary
{
    public partial class DiaryGameComponent
    {
        // Stable marker stamped into every suite event's gameContext so they can be identified in
        // saves or by future cleanup tooling. Purely a tag; no code reads it for behavior.
        private const string DevPromptSuiteMarker = "dev_prompt_suite=true";

        // Stable schema tokens reused below. Kept as const (not XML) per AGENTS.md: these are parser
        // sentinels that drive domain/template selection, not player-facing policy.
        private const string DevPromptSuiteInstructionKey = "PawnDiary.Dev.PromptSuite.Instruction";
        private const string DevPromptSuiteReasonKey = "PawnDiary.Dev.PromptSuite.Reason";

        /// <summary>
        /// Dev-only: creates one synthetic diary event per supported category for <paramref name="pawn"/>
        /// (and a second colonist for pair categories) and routes each through the generation queue. With
        /// prompt test mode enabled, each role is captured as a prompt_only card holding the exact prompt
        /// text that would have been sent to an LLM. Returns the number of synthetic events created
        /// (pair categories are skipped when no second colonist is available). Does nothing outside dev
        /// mode, for an ineligible pawn, or when prompt test mode is off — the UI handler enables it.
        /// </summary>
        public int GeneratePromptTestSuiteForDev(Pawn pawn, Pawn otherPawn)
        {
            if (!Prefs.DevMode || !PromptTestModeEnabled() || !IsDiaryEligible(pawn))
            {
                return 0;
            }

            // The pawn's per-pawn generation toggle must be on, otherwise DiaryGenerationEnabledFor
            // blocks the queue and no prompts are captured. This matches the existing mock-page helper's
            // expectation: the dev toggle above this button already controls it.
            if (!DiaryGenerationEnabledFor(pawn))
            {
                return 0;
            }

            Pawn pairPartner = ResolvePairPartner(pawn, otherPawn);
            List<DiaryEvent> created = new List<DiaryEvent>();

            // Pair categories. The selected pawn is the initiator; the partner is the recipient. Each
            // produces an initiator prompt-only card in the selected pawn's diary and a recipient card
            // in the partner's diary.
            if (pairPartner != null)
            {
                created.Add(AddInsultSuiteEvent(pawn, pairPartner));
                created.Add(AddSocialFightSuiteEvent(pawn, pairPartner));
                created.Add(AddRomanceSuiteEvent(pawn, pairPartner));
            }

            // Solo categories: single POV, live only in the selected pawn's diary. The gameContext
            // marker on each one drives the domain and template exactly as a real captured event would.
            created.Add(AddMentalBreakSuiteEvent(pawn));
            created.Add(AddHediffSuiteEvent(pawn));
            created.Add(AddInspirationSuiteEvent(pawn));
            created.Add(AddWorkSuiteEvent(pawn));
            created.Add(AddThoughtSuiteEvent(pawn));
            created.Add(AddMoodEventSuiteEvent(pawn));
            created.Add(AddTaleSuiteEvent(pawn));
            created.Add(AddDayReflectionSuiteEvent(pawn));

            QueueSuiteGenerations(created);
            return created.Count;
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

        /// <summary>
        /// Routes each synthetic event through the same dispatcher the tick scan uses. In prompt test
        /// mode QueuePrompt captures the prompt synchronously and marks the role prompt_only, so this
        /// returns with every card already captured (no async LLM work). Pair events need both roles
        /// queued explicitly because the initiator is captured (not completed) on its own pass.
        /// </summary>
        private void QueueSuiteGenerations(List<DiaryEvent> events)
        {
            if (events == null || events.Count == 0)
            {
                return;
            }

            Dictionary<string, Pawn> livePawnsById = SnapshotLivePawnsByLoadId();
            for (int i = 0; i < events.Count; i++)
            {
                DiaryEvent diaryEvent = events[i];
                if (diaryEvent == null)
                {
                    continue;
                }

                EnsureGenerationQueued(diaryEvent, DiaryEvent.InitiatorRole, null, livePawnsById);
                if (!diaryEvent.solo)
                {
                    EnsureGenerationQueued(diaryEvent, DiaryEvent.RecipientRole, null, livePawnsById);
                }
            }
        }

        // ---- shared localization helpers ----
        // Label/text/instruction reach the LLM prompt as evidence, so per AGENTS.md §12 they are
        // keyed (never hardcoded English). Resolve() returns the plain string from the TaggedString.

        private static string SuiteInstruction()
        {
            return DevPromptSuiteInstructionKey.Translate().Resolve();
        }

        private static string SuiteReason()
        {
            return DevPromptSuiteReasonKey.Translate().Resolve();
        }

        private static string SuiteLabel(string key)
        {
            return key.Translate().Resolve();
        }

        private static string SuiteText(string key, string name)
        {
            return key.Translate(name).Resolve();
        }

        // ---- pair categories ----

        // Interaction-domain insult. Classifies to the "insults" group (combat=true) so it routes to
        // PairCombat, and Insult is a real InteractionDef so the direct-speech instruction is included.
        private DiaryEvent AddInsultSuiteEvent(Pawn pawn, Pawn other)
        {
            string label = SuiteLabel("PawnDiary.Dev.PromptSuite.Insult.Label");
            string initiatorText = SuiteText("PawnDiary.Dev.PromptSuite.Insult.Initiator", other.LabelShortCap);
            string recipientText = SuiteText("PawnDiary.Dev.PromptSuite.Insult.Recipient", pawn.LabelShortCap);
            string context = "def=Insult; label=" + label + "; worker=Interaction_Insult; " + DevPromptSuiteMarker;
            return AddPairwiseEvent(pawn, other, "Insult", label, initiatorText, recipientText, SuiteInstruction(), context);
        }

        // MentalState-domain social fight. The mental_state= marker sets the domain to MentalState,
        // which GroupCombat treats as combat, so this also routes to PairCombat — but without the
        // direct-speech instruction (non-interaction source marker).
        private DiaryEvent AddSocialFightSuiteEvent(Pawn pawn, Pawn other)
        {
            string label = SuiteLabel("PawnDiary.Dev.PromptSuite.SocialFight.Label");
            string initiatorText = SuiteText("PawnDiary.Dev.PromptSuite.SocialFight.Initiator", other.LabelShortCap);
            string recipientText = SuiteText("PawnDiary.Dev.PromptSuite.SocialFight.Recipient", pawn.LabelShortCap);
            string context = "mental_state=SocialFighting; label=" + label + "; reason=" + SuiteReason() + "; " + DevPromptSuiteMarker;
            return AddPairwiseEvent(pawn, other, "SocialFighting", label, initiatorText, recipientText, SuiteInstruction(), context);
        }

        // Romance-domain marriage milestone. Classifies to "romance_relation" (important) so it routes
        // to PairImportant and carries the group's intimacy tone.
        private DiaryEvent AddRomanceSuiteEvent(Pawn pawn, Pawn other)
        {
            string label = SuiteLabel("PawnDiary.Dev.PromptSuite.Romance.Label");
            string initiatorText = SuiteText("PawnDiary.Dev.PromptSuite.Romance.Initiator", other.LabelShortCap);
            string recipientText = SuiteText("PawnDiary.Dev.PromptSuite.Romance.Recipient", pawn.LabelShortCap);
            string context = "romance=Spouse; label=" + label + "; kind=married; " + DevPromptSuiteMarker;
            return AddPairwiseEvent(pawn, other, "Spouse", label, initiatorText, recipientText, SuiteInstruction(), context);
        }

        // ---- solo categories ----

        // MentalState mental break. No internal-state marker, so template selection falls to group
        // importance: "mentalbreak" (catch-all) is important, so this routes to SoloImportant.
        private DiaryEvent AddMentalBreakSuiteEvent(Pawn pawn)
        {
            string label = SuiteLabel("PawnDiary.Dev.PromptSuite.MentalBreak.Label");
            string text = SuiteText("PawnDiary.Dev.PromptSuite.MentalBreak.Text", pawn.LabelShortCap);
            string context = "mental_state=Berserk; label=" + label + "; reason=" + SuiteReason() + "; " + DevPromptSuiteMarker;
            return AddSoloEvent(pawn, null, "Berserk", label, text, SuiteInstruction(), context);
        }

        // The remaining solo categories each carry one internal-state marker (hediff=, inspiration=,
        // work=, thought=, mood_event=) which routes them to SoloInternalState regardless of group.

        private DiaryEvent AddHediffSuiteEvent(Pawn pawn)
        {
            string label = SuiteLabel("PawnDiary.Dev.PromptSuite.Hediff.Label");
            string text = SuiteText("PawnDiary.Dev.PromptSuite.Hediff.Text", pawn.LabelShortCap);
            string context = "hediff=Flu; label=" + label + "; source=add; group=hediffMajorHealth; mode=Immediate; severity=0.45; stage=1; "
                + DevPromptSuiteMarker;
            return AddSoloEvent(pawn, null, "Flu", label, text, SuiteInstruction(), context);
        }

        private DiaryEvent AddInspirationSuiteEvent(Pawn pawn)
        {
            string label = SuiteLabel("PawnDiary.Dev.PromptSuite.Inspiration.Label");
            string text = SuiteText("PawnDiary.Dev.PromptSuite.Inspiration.Text", pawn.LabelShortCap);
            string context = "inspiration=Inspired_Recruitment; label=" + label + "; duration_days=8; " + DevPromptSuiteMarker;
            return AddSoloEvent(pawn, null, "Inspired_Recruitment", label, text, SuiteInstruction(), context);
        }

        private DiaryEvent AddWorkSuiteEvent(Pawn pawn)
        {
            string label = SuiteLabel("PawnDiary.Dev.PromptSuite.Work.Label");
            string text = SuiteText("PawnDiary.Dev.PromptSuite.Work.Text", pawn.LabelShortCap);
            string context = "work=Research; work_giver=DoResearch; mood_impact=positive; passion=true; low_skill=false; dumb_or_cleaning=false; dark_study=false; "
                + DevPromptSuiteMarker;
            return AddSoloEvent(pawn, null, PawnDiary.Capture.WorkEventData.PassionDefName, label, text, SuiteInstruction(), context);
        }

        private DiaryEvent AddThoughtSuiteEvent(Pawn pawn)
        {
            string label = SuiteLabel("PawnDiary.Dev.PromptSuite.Thought.Label");
            string text = SuiteText("PawnDiary.Dev.PromptSuite.Thought.Text", pawn.LabelShortCap);
            string context = "thought=AteWithoutTable; label=" + label + "; mood_impact=negative; mood_offset=-5; duration_days=1; "
                + DevPromptSuiteMarker;
            return AddSoloEvent(pawn, null, "AteWithoutTable", label, text, SuiteInstruction(), context);
        }

        private DiaryEvent AddMoodEventSuiteEvent(Pawn pawn)
        {
            string label = SuiteLabel("PawnDiary.Dev.PromptSuite.MoodEvent.Label");
            string text = SuiteText("PawnDiary.Dev.PromptSuite.MoodEvent.Text", pawn.LabelShortCap);
            string context = "mood_event=HeatWave; label=" + label + "; mood_impact=negative; " + DevPromptSuiteMarker;
            return AddSoloEvent(pawn, null, "HeatWave", label, text, SuiteInstruction(), context);
        }

        // Tale-domain research completion. Classifies to "talework" (important=false), so with no
        // internal-state marker this routes to SoloDefault — the one shape that exercises the plain
        // solo template without the "you" persona block.
        private DiaryEvent AddTaleSuiteEvent(Pawn pawn)
        {
            string label = SuiteLabel("PawnDiary.Dev.PromptSuite.Tale.Label");
            string text = SuiteText("PawnDiary.Dev.PromptSuite.Tale.Text", pawn.LabelShortCap);
            string context = "tale=FinishedResearchProject; label=" + label + "; taleClass=Tale_SinglePawn; " + DevPromptSuiteMarker;
            return AddSoloEvent(pawn, null, "FinishedResearchProject", label, text, SuiteInstruction(), context);
        }

        // Day reflection. The day_reflection=true marker routes to SoloDayReflection (a distinct system
        // prompt and field set) ahead of group-importance checks.
        private DiaryEvent AddDayReflectionSuiteEvent(Pawn pawn)
        {
            string label = SuiteLabel("PawnDiary.Dev.PromptSuite.DayReflection.Label");
            string text = SuiteText("PawnDiary.Dev.PromptSuite.DayReflection.Text", pawn.LabelShortCap);
            string context = "day_reflection=true; day=42; highlights=3; candidates=6; filler_moments=2; signals=health;work;social; "
                + DevPromptSuiteMarker;
            return AddSoloEvent(pawn, null, "PawnDiary_DayReflection", label, text, SuiteInstruction(), context);
        }
    }
}
