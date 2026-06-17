// Assembles the final prompt text sent to the model for each generation mode
// (single POV, paired sequential POV, solo). Static helpers, no state. Split out of
// DiaryGameComponent.cs. See DOCUMENTATION.md.
using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnDiary
{
    public static class DiaryPromptBuilder
    {
        private sealed class PromptContextPolicy
        {
            public bool includePawnSummary;
            public bool includeSetting;
            public bool includeTone;
            public bool includeRelationship;
            public bool includeLastOpener;
            public bool includePromptEnchantment;
            public bool includeWeapon;
            public bool includeHiddenInitiatorEntry;
        }

        // Prompts intentionally omit any field that is empty or "normal" (see AppendField),
        // so the model only ever sees signal — no "health: healthy", no weather indoors, etc.
        public static string BuildSequentialInteractionPrompt(DiaryEvent diaryEvent, string povRole, string personaRule, string promptEnchantment)
        {
            if (diaryEvent.solo)
            {
                return BuildSoloPrompt(diaryEvent, personaRule, promptEnchantment);
            }

            string initiatorEntry = string.Equals(povRole, DiaryEvent.RecipientRole, StringComparison.OrdinalIgnoreCase)
                ? DiaryContextBuilder.CleanLine(diaryEvent.initiatorGeneratedText)
                : null;

            return BuildPairPrompt(diaryEvent, povRole, initiatorEntry, personaRule, promptEnchantment);
        }

        public static string BuildInteractionPrompt(DiaryEvent diaryEvent, string povRole, string personaRule, string promptEnchantment)
        {
            if (diaryEvent.solo)
            {
                return BuildSoloPrompt(diaryEvent, personaRule, promptEnchantment);
            }

            return BuildPairPrompt(diaryEvent, povRole, null, personaRule, promptEnchantment);
        }

        /// <summary>
        /// Builds the neutral, persona-independent prompt used only for colonist death
        /// descriptions. It deliberately omits persona, relationship continuity, and first-person
        /// POV fields because this output is a factual death note, not a diary entry.
        /// </summary>
        public static string BuildDeathDescriptionPrompt(DiaryEvent diaryEvent)
        {
            string victimRole = ContextValue(diaryEvent.gameContext, "death_victim_role");
            string victimName = ContextValue(diaryEvent.gameContext, "death_victim");
            if (string.IsNullOrWhiteSpace(victimName))
            {
                victimName = NameForContextRole(diaryEvent, victimRole);
            }

            List<string> lines = new List<string> { "event: colonist death" };
            AppendField(lines, "deceased", victimName);
            AppendField(lines, "what happened", diaryEvent.neutralText);
            AppendField(lines, "death facts", BuildDeathFacts(diaryEvent.gameContext));
            AppendField(lines, "deceased pawn", PawnSummaryForContextRole(diaryEvent, victimRole));
            AppendField(lines, "setting", SurroundingsForContextRole(diaryEvent, victimRole));

            return string.Join("\n", lines.ToArray()) + "\n\n" + PawnDiarySettings.CurrentDeathDescriptionInstruction;
        }

        /// <summary>
        /// Builds the neutral, persona-independent prompt used for the first diary entry: how this
        /// pawn became part of the colony. Starting pawns get scenario context; later pawns get the
        /// SetFaction/join facts captured at runtime.
        /// </summary>
        public static string BuildArrivalDescriptionPrompt(DiaryEvent diaryEvent)
        {
            string pawnName = ContextValue(diaryEvent.gameContext, "arrival_pawn");
            if (string.IsNullOrWhiteSpace(pawnName))
            {
                pawnName = diaryEvent.initiatorName;
            }

            List<string> lines = new List<string> { "event: colonist arrival" };
            AppendField(lines, "colonist", pawnName);
            AppendField(lines, "what happened", diaryEvent.neutralText);
            AppendField(lines, "arrival facts", BuildArrivalFacts(diaryEvent.gameContext));
            AppendField(lines, "colonist pawn", diaryEvent.initiatorPawnSummary);
            AppendField(lines, "setting", diaryEvent.initiatorSurroundings);

            return string.Join("\n", lines.ToArray()) + "\n\n" + PawnDiarySettings.CurrentArrivalDescriptionInstruction;
        }

        /// <summary>
        /// Builds the user message for the title-generation follow-up call. The system
        /// prompt lives on the request and tells the model to return a title; the user message
        /// carries the diary entry to summarize. Uses the LLM-generated entry when available,
        /// else falls back to the raw game text. The title prompt is intentionally small and
        /// cheap — see <see cref="DiaryGameComponent.Generation.QueueTitleRequest"/>.
        /// </summary>
        public static string BuildTitlePrompt(DiaryEvent diaryEvent, string povRole)
        {
            if (diaryEvent == null)
            {
                return PawnDiarySettings.CurrentTitleUserInstruction;
            }

            // Prefer the polished LLM output; fall back to the raw game text when the main entry
            // hasn't finished yet (rare — the title call is only fired after a successful main
            // entry, but the fallback keeps the request self-contained).
            // DisplayTextForRole is the public accessor: returns generated if non-empty, else raw.
            string entryText = diaryEvent.DisplayTextForRole(povRole);
            if (string.IsNullOrWhiteSpace(entryText))
            {
                return PawnDiarySettings.CurrentTitleUserInstruction;
            }

            return entryText + "\n\n" + PawnDiarySettings.CurrentTitleUserInstruction;
        }

        /// <summary>
        /// Returns true when this non-neutral prompt can use a live prompt enchantment. These health
        /// hints travel with persona, while neutral chronicle/title prompts stay persona-free.
        /// </summary>
        public static bool ShouldResolvePromptEnchantment(DiaryEvent diaryEvent)
        {
            return PolicyFor(diaryEvent, diaryEvent != null && !diaryEvent.solo).includePromptEnchantment;
        }

        private static string BuildPairPrompt(DiaryEvent diaryEvent, string povRole, string initiatorEntry, string personaRule, string promptEnchantment)
        {
            bool isInitiator = string.Equals(povRole, DiaryEvent.InitiatorRole, StringComparison.OrdinalIgnoreCase);
            string otherName = isInitiator ? diaryEvent.recipientName : diaryEvent.initiatorName;
            string povText = diaryEvent.TextForRole(povRole);
            string povSummary = isInitiator ? diaryEvent.initiatorPawnSummary : diaryEvent.recipientPawnSummary;
            PromptContextPolicy policy = PolicyFor(diaryEvent, hasOtherPawn: true);
            string effectiveInitiatorEntry = policy.includeHiddenInitiatorEntry ? initiatorEntry : null;

            List<string> lines = new List<string> { "event: " + EventNoun(diaryEvent) };

            AppendField(lines, "pov", diaryEvent.NameForRole(povRole));
            AppendField(lines, "role", isInitiator ? "initiator" : "recipient");
            AppendField(lines, "with", otherName);
            AppendField(lines, "what you saw", povText);
            AppendField(lines, "instruction", diaryEvent.instruction);
            if (policy.includePawnSummary)
            {
                AppendField(lines, "you", povSummary);
            }

            // Persona is a writing-style rule from the pawn's saved preset, not a gameplay fact.
            AppendField(lines, "persona", personaRule);
            // Prompt enchantments are optional live health-condition hints chosen from weighted XML.
            if (policy.includePromptEnchantment)
            {
                AppendField(lines, "important health", promptEnchantment);
            }

            if (policy.includeSetting)
            {
                AppendField(lines, "setting", diaryEvent.SurroundingsForRole(povRole));
            }

            // Tone is the event's emotional register (terrifying, funny, tender...), set per group.
            if (policy.includeTone)
            {
                AppendField(lines, "tone", diaryEvent.ToneDirective());
            }

            if (policy.includeRelationship)
            {
                AppendField(lines, "relationship", diaryEvent.ContinuityForRole(povRole));
            }

            if (policy.includeLastOpener)
            {
                AppendField(lines, "my last opener (not repeat)", diaryEvent.LastOpenerForRole(povRole));
            }

            // Weapon context is deliberately narrow: it helps combat entries, but distracts routine
            // small-model prompts.
            if (policy.includeWeapon)
            {
                AppendField(lines, "weapon", isInitiator ? diaryEvent.initiatorWeapon : diaryEvent.recipientWeapon);
            }

            AppendField(lines, "initiator diary (hidden context)", effectiveInitiatorEntry);

            string instruction = string.IsNullOrWhiteSpace(effectiveInitiatorEntry)
                ? PawnDiarySettings.CurrentSinglePovInstruction
                : PawnDiarySettings.CurrentRecipientFollowupInstruction;
            instruction = AppendPairDirectSpeechInstruction(diaryEvent, povRole, instruction);

            return string.Join("\n", lines.ToArray()) + "\n\n" + instruction;
        }

        private static string BuildSoloPrompt(DiaryEvent diaryEvent, string personaRule, string promptEnchantment)
        {
            PromptContextPolicy policy = PolicyFor(diaryEvent, hasOtherPawn: false);
            List<string> lines = new List<string> { "event: " + EventNoun(diaryEvent) };

            AppendField(lines, "pov", diaryEvent.initiatorName);
            AppendField(lines, "what happened", diaryEvent.initiatorText);
            AppendField(lines, "instruction", diaryEvent.instruction);
            if (policy.includePawnSummary)
            {
                AppendField(lines, "you", diaryEvent.initiatorPawnSummary);
            }

            // Solo events use the same persona field as pairwise entries for prompt consistency.
            AppendField(lines, "persona", personaRule);
            // Prompt enchantments are optional live health-condition hints chosen from weighted XML.
            if (policy.includePromptEnchantment)
            {
                AppendField(lines, "important health", promptEnchantment);
            }

            if (policy.includeSetting)
            {
                AppendField(lines, "setting", diaryEvent.initiatorSurroundings);
            }

            // Tone is the event's emotional register (terrifying, funny, tender...), set per group.
            if (policy.includeTone)
            {
                AppendField(lines, "tone", diaryEvent.ToneDirective());
            }

            if (policy.includeRelationship)
            {
                AppendField(lines, "relationship", diaryEvent.initiatorContinuity);
            }

            if (policy.includeLastOpener)
            {
                AppendField(lines, "my last opener (not repeat)", diaryEvent.initiatorLastOpener);
            }

            if (policy.includeWeapon)
            {
                AppendField(lines, "weapon", diaryEvent.initiatorWeapon);
            }

            string instruction = AppendSoloInteractionDirectSpeechInstruction(diaryEvent,
                PawnDiarySettings.CurrentSinglePovInstruction);

            return string.Join("\n", lines.ToArray()) + "\n\n" + instruction;
        }

        private static PromptContextPolicy PolicyFor(DiaryEvent diaryEvent, bool hasOtherPawn)
        {
            PromptContextPolicy policy = new PromptContextPolicy();
            if (diaryEvent == null)
            {
                return policy;
            }

            // Every first-person prompt carries persona, current surroundings, a compact continuity
            // cue that prevents repeated openings, and one compact live hediff hint. The rest of this
            // policy only decides which broader context fields join them.
            policy.includeLastOpener = true;
            policy.includePromptEnchantment = true;
            policy.includeSetting = true;

            bool combat = diaryEvent.ShouldShowWeapon();
            bool important = diaryEvent.IsImportant();
            bool batched = HasContext(diaryEvent, "batch=");
            bool dayReflection = diaryEvent.IsDayReflection();
            bool internalState = HasContext(diaryEvent, "mood_event=")
                || HasContext(diaryEvent, "thought=")
                || HasContext(diaryEvent, "inspiration=")
                || HasContext(diaryEvent, "work=");

            if (combat)
            {
                policy.includePawnSummary = true;
                policy.includeSetting = true;
                policy.includeTone = true;
                policy.includeRelationship = hasOtherPawn;
                policy.includeWeapon = true;
                policy.includeHiddenInitiatorEntry = true;
                return policy;
            }

            if (dayReflection)
            {
                policy.includePawnSummary = true;
                return policy;
            }

            if (hasOtherPawn && !batched)
            {
                policy.includeRelationship = true;
                policy.includeTone = important;
                policy.includeHiddenInitiatorEntry = important;
                return policy;
            }

            if (internalState || batched)
            {
                return policy;
            }

            // Major solo events still benefit from grounding, but avoid the old broad
            // relationship/opener pile-up.
            if (important)
            {
                policy.includePawnSummary = true;
                policy.includeSetting = true;
                policy.includeTone = true;
            }

            return policy;
        }

        private static string EventNoun(DiaryEvent diaryEvent)
        {
            string label = DiaryContextBuilder.CleanLine(diaryEvent.interactionLabel);
            if (string.IsNullOrWhiteSpace(label))
            {
                return "PawnDiary.Prompt.SocialEvent".Translate();
            }

            return label.ToLowerInvariant();
        }

        private static string NameForContextRole(DiaryEvent diaryEvent, string role)
        {
            if (string.Equals(role, DiaryEvent.RecipientRole, StringComparison.OrdinalIgnoreCase))
            {
                return diaryEvent.recipientName;
            }

            return diaryEvent.initiatorName;
        }

        private static string PawnSummaryForContextRole(DiaryEvent diaryEvent, string role)
        {
            if (string.Equals(role, DiaryEvent.RecipientRole, StringComparison.OrdinalIgnoreCase))
            {
                return diaryEvent.recipientPawnSummary;
            }

            return diaryEvent.initiatorPawnSummary;
        }

        private static string SurroundingsForContextRole(DiaryEvent diaryEvent, string role)
        {
            if (string.Equals(role, DiaryEvent.RecipientRole, StringComparison.OrdinalIgnoreCase))
            {
                return diaryEvent.recipientSurroundings;
            }

            return diaryEvent.initiatorSurroundings;
        }

        private static string BuildDeathFacts(string context)
        {
            List<string> parts = new List<string>();
            AddContextFact(parts, context, "damage", "damage");
            AddContextFact(parts, context, "hitPart", "hit part");
            AddContextFact(parts, context, "instigator", "instigator");
            AddContextFact(parts, context, "weapon", "weapon");
            AddContextFact(parts, context, "tool", "tool");
            AddContextFact(parts, context, "culprit", "condition");
            AddContextFact(parts, context, "culpritPart", "condition part");
            AddContextFact(parts, context, "destroyed_or_missing_parts", "destroyed/missing");
            AddContextFact(parts, context, "other_lethal_conditions", "other conditions");
            AddContextFact(parts, context, "other_pawn", "other pawn");
            AddContextFact(parts, context, "death_surroundings", "surroundings");
            return string.Join("; ", parts.ToArray());
        }

        private static string BuildArrivalFacts(string context)
        {
            List<string> parts = new List<string>();
            AddContextFact(parts, context, "arrival_source", "source");
            AddContextFact(parts, context, "scenario_name", "scenario");
            AddContextFact(parts, context, "scenario_description", "scenario detail");
            AddContextFact(parts, context, "priorFaction", "prior faction");
            AddContextFact(parts, context, "pawnKind", "pawn kind");
            AddContextFact(parts, context, "recruiter", "recruiter");
            AddContextFact(parts, context, "creepjoiner", "creepjoiner");
            AddContextFact(parts, context, "arrival_surroundings", "surroundings");
            return string.Join("; ", parts.ToArray());
        }

        private static void AddContextFact(List<string> parts, string context, string key, string label)
        {
            string value = ContextValue(context, key);
            if (!string.IsNullOrWhiteSpace(value))
            {
                parts.Add(label + "=" + value);
            }
        }

        private static bool HasContext(DiaryEvent diaryEvent, string marker)
        {
            return diaryEvent != null
                && !string.IsNullOrWhiteSpace(diaryEvent.gameContext)
                && diaryEvent.gameContext.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string AppendPairDirectSpeechInstruction(DiaryEvent diaryEvent, string povRole, string instruction)
        {
            if (diaryEvent == null || diaryEvent.solo || !IsInteractionPrompt(diaryEvent))
            {
                return instruction;
            }

            bool isInitiator = string.Equals(povRole, DiaryEvent.InitiatorRole, StringComparison.OrdinalIgnoreCase);
            string povName = diaryEvent.NameForRole(povRole);
            string otherName = isInitiator ? diaryEvent.recipientName : diaryEvent.initiatorName;
            string key = isInitiator
                ? "PawnDiary.Prompt.PairDirectSpeechInstruction.Initiator"
                : "PawnDiary.Prompt.PairDirectSpeechInstruction.Recipient";
            return AppendLocalizedInstruction(instruction, key, povName, otherName);
        }

        private static string AppendSoloInteractionDirectSpeechInstruction(DiaryEvent diaryEvent, string instruction)
        {
            if (!ShouldOfferSoloInteractionDirectSpeech(diaryEvent))
            {
                return instruction;
            }

            return AppendLocalizedInstruction(instruction, "PawnDiary.Prompt.SoloInteractionDirectSpeechInstruction",
                diaryEvent.initiatorName);
        }

        private static bool ShouldOfferSoloInteractionDirectSpeech(DiaryEvent diaryEvent)
        {
            return diaryEvent != null
                && diaryEvent.solo
                && IsInteractionPrompt(diaryEvent);
        }

        private static bool IsInteractionPrompt(DiaryEvent diaryEvent)
        {
            if (diaryEvent == null)
            {
                return false;
            }

            if (HasContext(diaryEvent, "batch=ambient_day_note")
                || HasContext(diaryEvent, "arrival_description=")
                || HasContext(diaryEvent, "death_description=")
                || HasContext(diaryEvent, "dev_mock=")
                || HasContext(diaryEvent, "mental_state=")
                || HasContext(diaryEvent, "tale=")
                || HasContext(diaryEvent, "mood_event=")
                || HasContext(diaryEvent, "thought=")
                || HasContext(diaryEvent, "inspiration=")
                || HasContext(diaryEvent, "work=")
                || HasContext(diaryEvent, "hediff=")
                || HasContext(diaryEvent, "day_reflection="))
            {
                return false;
            }

            // Old saves may only have fallback "def=/label=" context, so verify the saved defName
            // is still a real InteractionDef before adding dialogue guidance.
            return HasContext(diaryEvent, "batch=interaction")
                || DefDatabase<InteractionDef>.GetNamedSilentFail(diaryEvent.interactionDefName) != null;
        }

        private static string AppendLocalizedInstruction(string instruction, string key)
        {
            // Keep this outside the editable base prompt so old saves with custom prompts still get
            // interaction-specific dialogue cues. Queueing happens on the main thread, so Translate
            // is safe here.
            return AppendInstructionText(instruction, key.Translate().Resolve());
        }

        private static string AppendLocalizedInstruction(string instruction, string key, string arg0)
        {
            return AppendInstructionText(instruction, key.Translate(arg0).Resolve());
        }

        private static string AppendLocalizedInstruction(string instruction, string key, string arg0, string arg1)
        {
            return AppendInstructionText(instruction, key.Translate(arg0, arg1).Resolve());
        }

        private static string AppendInstructionText(string instruction, string extraInstruction)
        {
            if (string.IsNullOrWhiteSpace(extraInstruction))
            {
                return instruction;
            }

            if (string.IsNullOrWhiteSpace(instruction))
            {
                return extraInstruction;
            }

            return instruction.TrimEnd() + " " + extraInstruction;
        }

        private static string ContextValue(string context, string key)
        {
            if (string.IsNullOrWhiteSpace(context) || string.IsNullOrWhiteSpace(key))
            {
                return string.Empty;
            }

            string prefix = key + "=";
            string[] parts = context.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i].Trim();
                if (part.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return part.Substring(prefix.Length).Trim();
                }
            }

            return string.Empty;
        }

        // Adds "key: value" only when the value carries real signal. Empty strings and
        // placeholder values ("none", "n/a", "unknown") are skipped so they cost no tokens.
        private static void AppendField(List<string> lines, string key, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            string trimmed = value.Trim();
            if (trimmed == "none" || trimmed == "n/a" || trimmed == "unknown")
            {
                return;
            }

            lines.Add(key + ": " + trimmed);
        }
    }
}
