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
        private sealed class PromptRenderContext
        {
            public DiaryEvent diaryEvent;
            public string povRole;
            public bool hasOtherPawn;
            public bool isInitiator;
            public string otherName;
            public string povText;
            public string povName;
            public string povSummary;
            public string setting;
            public string relationship;
            public string lastOpener;
            public string weapon;
            public string personaRule;
            public string promptEnchantment;
            public string hiddenInitiatorEntry;
            public string entryText;
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
            return RenderTemplate(
                DiaryPromptTemplates.DeathDescription,
                BuildNeutralRenderContext(diaryEvent, DiaryEvent.NeutralRole, null),
                DiaryPromptTemplates.FinalInstructionFor(DiaryPromptTemplates.DeathDescription));
        }

        /// <summary>
        /// Builds the neutral, persona-independent prompt used for the first diary entry: how this
        /// pawn became part of the colony. Starting pawns get scenario context; later pawns get the
        /// SetFaction/join facts captured at runtime.
        /// </summary>
        public static string BuildArrivalDescriptionPrompt(DiaryEvent diaryEvent)
        {
            return RenderTemplate(
                DiaryPromptTemplates.ArrivalDescription,
                BuildNeutralRenderContext(diaryEvent, DiaryEvent.InitiatorRole, null),
                DiaryPromptTemplates.FinalInstructionFor(DiaryPromptTemplates.ArrivalDescription));
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
                return DiaryPromptTemplates.FinalInstructionFor(DiaryPromptTemplates.Title);
            }

            // Prefer the polished LLM output; fall back to the raw game text when the main entry
            // hasn't finished yet (rare — the title call is only fired after a successful main
            // entry, but the fallback keeps the request self-contained).
            // DisplayTextForRole is the public accessor: returns generated if non-empty, else raw.
            string entryText = diaryEvent.DisplayTextForRole(povRole);
            if (string.IsNullOrWhiteSpace(entryText))
            {
                return DiaryPromptTemplates.FinalInstructionFor(DiaryPromptTemplates.Title);
            }

            PromptRenderContext context = BuildNeutralRenderContext(diaryEvent, povRole, entryText);
            return RenderTemplate(
                DiaryPromptTemplates.Title,
                context,
                DiaryPromptTemplates.FinalInstructionFor(DiaryPromptTemplates.Title));
        }

        /// <summary>
        /// Returns true when this non-neutral prompt can use a live prompt enchantment. These health
        /// hints travel with persona, while neutral chronicle/title prompts stay persona-free.
        /// </summary>
        public static bool ShouldResolvePromptEnchantment(DiaryEvent diaryEvent)
        {
            string templateKey = TemplateKeyFor(diaryEvent, diaryEvent != null && !diaryEvent.solo);
            return DiaryPromptTemplates.ForKey(templateKey).includePromptEnchantment;
        }

        /// <summary>
        /// Returns the XML template's system prompt for this event shape, with DiaryPromptDef as the
        /// fallback source for templates that only specify field lists.
        /// </summary>
        public static string SystemPromptForEvent(DiaryEvent diaryEvent)
        {
            if (diaryEvent != null && diaryEvent.HasDeathDescription())
            {
                return DiaryPromptTemplates.SystemPromptFor(DiaryPromptTemplates.DeathDescription);
            }

            if (diaryEvent != null && diaryEvent.HasArrivalDescription())
            {
                return DiaryPromptTemplates.SystemPromptFor(DiaryPromptTemplates.ArrivalDescription);
            }

            return DiaryPromptTemplates.SystemPromptFor(
                TemplateKeyFor(diaryEvent, diaryEvent != null && !diaryEvent.solo));
        }

        /// <summary>
        /// Returns the XML-configured system prompt for the title follow-up request.
        /// </summary>
        public static string TitleSystemPrompt()
        {
            return DiaryPromptTemplates.SystemPromptFor(DiaryPromptTemplates.Title);
        }

        private static string BuildPairPrompt(DiaryEvent diaryEvent, string povRole, string initiatorEntry, string personaRule, string promptEnchantment)
        {
            bool isInitiator = string.Equals(povRole, DiaryEvent.InitiatorRole, StringComparison.OrdinalIgnoreCase);
            string templateKey = TemplateKeyFor(diaryEvent, hasOtherPawn: true);
            PromptRenderContext context = BuildPairRenderContext(
                diaryEvent,
                povRole,
                isInitiator,
                personaRule,
                promptEnchantment,
                initiatorEntry);

            string instruction = string.IsNullOrWhiteSpace(initiatorEntry)
                ? DiaryPromptTemplates.FinalInstructionFor(templateKey)
                : DiaryPromptTemplates.RecipientFinalInstruction(templateKey);
            if (DiaryPromptTemplates.ForKey(templateKey).appendDirectSpeechInstruction)
            {
                instruction = AppendPairDirectSpeechInstruction(diaryEvent, povRole, instruction);
            }

            return RenderTemplate(templateKey, context, instruction);
        }

        private static string BuildSoloPrompt(DiaryEvent diaryEvent, string personaRule, string promptEnchantment)
        {
            string templateKey = TemplateKeyFor(diaryEvent, hasOtherPawn: false);
            string instruction = DiaryPromptTemplates.FinalInstructionFor(templateKey);
            if (DiaryPromptTemplates.ForKey(templateKey).appendDirectSpeechInstruction)
            {
                instruction = AppendSoloInteractionDirectSpeechInstruction(diaryEvent, instruction);
            }

            return RenderTemplate(
                templateKey,
                BuildSoloRenderContext(diaryEvent, personaRule, promptEnchantment),
                instruction);
        }

        private static string TemplateKeyFor(DiaryEvent diaryEvent, bool hasOtherPawn)
        {
            if (diaryEvent == null)
            {
                return DiaryPromptTemplates.SoloDefault;
            }

            bool combat = diaryEvent.ShouldShowWeapon();
            bool important = diaryEvent.IsImportant();
            bool batched = HasContext(diaryEvent, "batch=");
            bool dayReflection = diaryEvent.IsDayReflection();
            bool internalState = HasContext(diaryEvent, "mood_event=")
                || HasContext(diaryEvent, "thought=")
                || HasContext(diaryEvent, "inspiration=")
                || HasContext(diaryEvent, "work=")
                || HasContext(diaryEvent, "hediff=");

            if (hasOtherPawn)
            {
                if (combat)
                {
                    return DiaryPromptTemplates.PairCombat;
                }

                if (batched)
                {
                    return DiaryPromptTemplates.PairBatched;
                }

                return important ? DiaryPromptTemplates.PairImportant : DiaryPromptTemplates.PairDefault;
            }

            if (dayReflection)
            {
                return DiaryPromptTemplates.SoloDayReflection;
            }

            if (internalState)
            {
                return DiaryPromptTemplates.SoloInternalState;
            }

            if (batched)
            {
                return DiaryPromptTemplates.SoloBatched;
            }

            return important ? DiaryPromptTemplates.SoloImportant : DiaryPromptTemplates.SoloDefault;
        }

        private static PromptRenderContext BuildPairRenderContext(DiaryEvent diaryEvent, string povRole,
            bool isInitiator, string personaRule, string promptEnchantment, string initiatorEntry)
        {
            return new PromptRenderContext
            {
                diaryEvent = diaryEvent,
                povRole = povRole,
                hasOtherPawn = true,
                isInitiator = isInitiator,
                otherName = isInitiator ? diaryEvent.recipientName : diaryEvent.initiatorName,
                povText = diaryEvent.TextForRole(povRole),
                povName = diaryEvent.NameForRole(povRole),
                povSummary = isInitiator ? diaryEvent.initiatorPawnSummary : diaryEvent.recipientPawnSummary,
                setting = diaryEvent.SurroundingsForRole(povRole),
                relationship = diaryEvent.ContinuityForRole(povRole),
                lastOpener = diaryEvent.LastOpenerForRole(povRole),
                weapon = isInitiator ? diaryEvent.initiatorWeapon : diaryEvent.recipientWeapon,
                personaRule = personaRule,
                promptEnchantment = promptEnchantment,
                hiddenInitiatorEntry = initiatorEntry
            };
        }

        private static PromptRenderContext BuildSoloRenderContext(DiaryEvent diaryEvent, string personaRule,
            string promptEnchantment)
        {
            return new PromptRenderContext
            {
                diaryEvent = diaryEvent,
                povRole = DiaryEvent.InitiatorRole,
                hasOtherPawn = false,
                isInitiator = true,
                povText = diaryEvent.initiatorText,
                povName = diaryEvent.initiatorName,
                povSummary = diaryEvent.initiatorPawnSummary,
                setting = diaryEvent.initiatorSurroundings,
                relationship = diaryEvent.initiatorContinuity,
                lastOpener = diaryEvent.initiatorLastOpener,
                weapon = diaryEvent.initiatorWeapon,
                personaRule = personaRule,
                promptEnchantment = promptEnchantment
            };
        }

        private static PromptRenderContext BuildNeutralRenderContext(DiaryEvent diaryEvent, string povRole,
            string entryText)
        {
            if (diaryEvent == null)
            {
                return new PromptRenderContext { entryText = entryText };
            }

            bool recipient = string.Equals(povRole, DiaryEvent.RecipientRole, StringComparison.OrdinalIgnoreCase);
            return new PromptRenderContext
            {
                diaryEvent = diaryEvent,
                povRole = povRole,
                hasOtherPawn = !diaryEvent.solo,
                isInitiator = !recipient,
                povText = diaryEvent.TextForRole(povRole),
                povName = diaryEvent.NameForRole(povRole),
                povSummary = recipient ? diaryEvent.recipientPawnSummary : diaryEvent.initiatorPawnSummary,
                setting = recipient ? diaryEvent.recipientSurroundings : diaryEvent.initiatorSurroundings,
                relationship = diaryEvent.ContinuityForRole(povRole),
                lastOpener = diaryEvent.LastOpenerForRole(povRole),
                weapon = recipient ? diaryEvent.recipientWeapon : diaryEvent.initiatorWeapon,
                entryText = entryText
            };
        }

        private static string RenderTemplate(string templateKey, PromptRenderContext context, string instruction)
        {
            DiaryPromptTemplateDef template = DiaryPromptTemplates.ForKey(templateKey);
            List<string> lines = new List<string>();
            List<DiaryPromptFieldDef> fields = template.fields;
            if (fields == null || fields.Count == 0)
            {
                fields = DiaryPromptTemplates.ForKey(templateKey).fields;
            }

            for (int i = 0; i < fields.Count; i++)
            {
                DiaryPromptFieldDef field = fields[i];
                if (field == null || !field.enabled)
                {
                    continue;
                }

                string label = string.IsNullOrWhiteSpace(field.label) ? field.source : field.label;
                AppendField(lines, label, ResolveField(field, context));
            }

            string body = string.Join("\n", lines.ToArray());
            if (string.IsNullOrWhiteSpace(instruction))
            {
                return body;
            }

            if (string.IsNullOrWhiteSpace(body))
            {
                return instruction;
            }

            return body + "\n\n" + instruction;
        }

        private static string ResolveField(DiaryPromptFieldDef field, PromptRenderContext context)
        {
            if (field == null || context == null)
            {
                return string.Empty;
            }

            DiaryEvent diaryEvent = context.diaryEvent;
            string source = field.source ?? string.Empty;
            if (source.Equals("EventNoun", StringComparison.OrdinalIgnoreCase))
            {
                return EventNoun(diaryEvent);
            }

            if (source.Equals("PovName", StringComparison.OrdinalIgnoreCase))
            {
                return context.povName;
            }

            if (source.Equals("PovRole", StringComparison.OrdinalIgnoreCase))
            {
                return context.isInitiator ? "initiator" : "recipient";
            }

            if (source.Equals("OtherPawnName", StringComparison.OrdinalIgnoreCase))
            {
                return context.otherName;
            }

            if (source.Equals("PovText", StringComparison.OrdinalIgnoreCase)
                || source.Equals("WhatHappened", StringComparison.OrdinalIgnoreCase)
                || source.Equals("WhatYouSaw", StringComparison.OrdinalIgnoreCase))
            {
                return context.povText;
            }

            if (source.Equals("NeutralText", StringComparison.OrdinalIgnoreCase))
            {
                return diaryEvent?.neutralText;
            }

            if (source.Equals("Instruction", StringComparison.OrdinalIgnoreCase))
            {
                return diaryEvent?.instruction;
            }

            if (source.Equals("PawnSummary", StringComparison.OrdinalIgnoreCase))
            {
                return context.povSummary;
            }

            if (source.Equals("Persona", StringComparison.OrdinalIgnoreCase))
            {
                return context.personaRule;
            }

            if (source.Equals("PromptEnchantment", StringComparison.OrdinalIgnoreCase))
            {
                return context.promptEnchantment;
            }

            if (source.Equals("Setting", StringComparison.OrdinalIgnoreCase))
            {
                return context.setting;
            }

            if (source.Equals("Tone", StringComparison.OrdinalIgnoreCase))
            {
                return diaryEvent?.ToneDirective();
            }

            if (source.Equals("Relationship", StringComparison.OrdinalIgnoreCase))
            {
                return context.relationship;
            }

            if (source.Equals("LastOpener", StringComparison.OrdinalIgnoreCase))
            {
                return context.lastOpener;
            }

            if (source.Equals("Weapon", StringComparison.OrdinalIgnoreCase))
            {
                return context.weapon;
            }

            if (source.Equals("HiddenInitiatorEntry", StringComparison.OrdinalIgnoreCase))
            {
                return context.hiddenInitiatorEntry;
            }

            if (source.Equals("DeathVictim", StringComparison.OrdinalIgnoreCase))
            {
                string victimRole = DiaryContextFields.Value(diaryEvent?.gameContext, "death_victim_role");
                string victimName = DiaryContextFields.Value(diaryEvent?.gameContext, "death_victim");
                return string.IsNullOrWhiteSpace(victimName) ? NameForContextRole(diaryEvent, victimRole) : victimName;
            }

            if (source.Equals("DeathFacts", StringComparison.OrdinalIgnoreCase))
            {
                return BuildDeathFacts(diaryEvent?.gameContext);
            }

            if (source.Equals("DeathPawnSummary", StringComparison.OrdinalIgnoreCase))
            {
                string victimRole = DiaryContextFields.Value(diaryEvent?.gameContext, "death_victim_role");
                return PawnSummaryForContextRole(diaryEvent, victimRole);
            }

            if (source.Equals("DeathSetting", StringComparison.OrdinalIgnoreCase))
            {
                string victimRole = DiaryContextFields.Value(diaryEvent?.gameContext, "death_victim_role");
                return SurroundingsForContextRole(diaryEvent, victimRole);
            }

            if (source.Equals("ArrivalPawn", StringComparison.OrdinalIgnoreCase))
            {
                string pawnName = DiaryContextFields.Value(diaryEvent?.gameContext, "arrival_pawn");
                return string.IsNullOrWhiteSpace(pawnName) ? diaryEvent?.initiatorName : pawnName;
            }

            if (source.Equals("ArrivalFacts", StringComparison.OrdinalIgnoreCase))
            {
                return BuildArrivalFacts(diaryEvent?.gameContext);
            }

            if (source.Equals("EntryText", StringComparison.OrdinalIgnoreCase))
            {
                return context.entryText;
            }

            if (source.Equals("GameContext", StringComparison.OrdinalIgnoreCase))
            {
                return DiaryContextFields.Value(diaryEvent?.gameContext, field.contextKey);
            }

            return string.Empty;
        }

        private static string EventNoun(DiaryEvent diaryEvent)
        {
            if (diaryEvent == null)
            {
                return "PawnDiary.Prompt.SocialEvent".Translate();
            }

            string label = DiaryContextBuilder.CleanLine(diaryEvent.interactionLabel);
            if (string.IsNullOrWhiteSpace(label))
            {
                return "PawnDiary.Prompt.SocialEvent".Translate();
            }

            return label.ToLowerInvariant();
        }

        private static string NameForContextRole(DiaryEvent diaryEvent, string role)
        {
            if (diaryEvent == null)
            {
                return string.Empty;
            }

            if (string.Equals(role, DiaryEvent.RecipientRole, StringComparison.OrdinalIgnoreCase))
            {
                return diaryEvent.recipientName;
            }

            return diaryEvent.initiatorName;
        }

        private static string PawnSummaryForContextRole(DiaryEvent diaryEvent, string role)
        {
            if (diaryEvent == null)
            {
                return string.Empty;
            }

            if (string.Equals(role, DiaryEvent.RecipientRole, StringComparison.OrdinalIgnoreCase))
            {
                return diaryEvent.recipientPawnSummary;
            }

            return diaryEvent.initiatorPawnSummary;
        }

        private static string SurroundingsForContextRole(DiaryEvent diaryEvent, string role)
        {
            if (diaryEvent == null)
            {
                return string.Empty;
            }

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
            string value = DiaryContextFields.Value(context, key);
            if (!string.IsNullOrWhiteSpace(value))
            {
                parts.Add(label + "=" + value);
            }
        }

        private static bool HasContext(DiaryEvent diaryEvent, string marker)
        {
            return diaryEvent != null
                && DiaryContextFields.HasMarker(diaryEvent.gameContext, marker);
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
