// Pure prompt planning for the diary pipeline.
//
// This file is allowed to depend on other pure helpers such as PromptAssembler and
// DiaryContextFields. It must not read RimWorld state, XML DefDatabase, settings, Translate(), RNG,
// IO, Verse, or UnityEngine. Impure callers pass in DiaryEventPayload and DiaryPolicySnapshot.
using System;
using System.Collections.Generic;

namespace PawnDiary
{
    /// <summary>
    /// Converts a plain event payload plus a plain policy snapshot into the exact prompt envelope sent
    /// to the LLM transport layer.
    /// </summary>
    public static class DiaryPromptPlanner
    {
        public static DiaryPromptPlan Build(DiaryPromptRequest request)
        {
            if (request == null)
            {
                return EmptyPlan();
            }

            DiaryEventPayload payload = request.payload ?? new DiaryEventPayload();
            DiaryPolicySnapshot policy = request.policy ?? new DiaryPolicySnapshot();
            string templateKey = TemplateKeyFor(request);
            DiaryTemplatePolicy template = policy.Template(templateKey);
            string finalInstruction = FinalInstructionFor(request, template);
            PromptValues values = ProjectValues(template, payload, request);

            string userPrompt = PromptAssembler.RenderUserPrompt(
                ToAssemblerFields(template.fields),
                values,
                finalInstruction);

            string systemPrompt = PromptAssembler.ComposeSystem(
                template.systemPrompt,
                request.personaVoiceBlock,
                template.includePersona);

            int responseMaxTokens = request.maxTokens > 0 ? request.maxTokens : template.maxTokens;
            return new DiaryPromptPlan
            {
                eventId = payload.eventId,
                povRole = request.povRole,
                templateKey = templateKey,
                forcedModelName = policy.group?.forcedModelName,
                systemPrompt = systemPrompt,
                userPrompt = userPrompt,
                debugLabel = templateKey + ":" + (request.povRole ?? string.Empty),
                responseRules = DiaryResponseRules.ForRequest(payload.eventId, request.povRole,
                    request.titleRequest, responseMaxTokens)
            };
        }

        public static string TemplateKeyFor(DiaryPromptRequest request)
        {
            if (request == null)
            {
                return DiaryPipelineTemplates.SoloDefault;
            }

            if (request.titleRequest)
            {
                return DiaryPipelineTemplates.Title;
            }

            DiaryEventPayload payload = request.payload;
            if (payload == null)
            {
                return DiaryPipelineTemplates.SoloDefault;
            }

            if (payload.hasDeathDescription)
            {
                return DiaryPipelineTemplates.DeathDescription;
            }

            if (payload.hasArrivalDescription)
            {
                return DiaryPipelineTemplates.ArrivalDescription;
            }

            bool hasOtherPawn = !payload.solo;
            bool combat = request.policy?.group != null && request.policy.group.combat;
            bool important = request.policy?.group == null || request.policy.group.important;
            bool batched = HasContext(payload, "batch=");
            bool internalState = HasContext(payload, "mood_event=")
                || HasContext(payload, "thought=")
                || HasContext(payload, "inspiration=")
                || HasContext(payload, "work=")
                || HasContext(payload, "hediff=");

            if (hasOtherPawn)
            {
                if (combat)
                {
                    return DiaryPipelineTemplates.PairCombat;
                }

                if (batched)
                {
                    return DiaryPipelineTemplates.PairBatched;
                }

                return important ? DiaryPipelineTemplates.PairImportant : DiaryPipelineTemplates.PairDefault;
            }

            if (payload.quadrumReflection)
            {
                return DiaryPipelineTemplates.SoloQuadrumReflection;
            }

            if (payload.dayReflection)
            {
                return DiaryPipelineTemplates.SoloDayReflection;
            }

            if (internalState)
            {
                return DiaryPipelineTemplates.SoloInternalState;
            }

            if (batched)
            {
                return combat ? DiaryPipelineTemplates.SoloImportant : DiaryPipelineTemplates.SoloBatched;
            }

            return important ? DiaryPipelineTemplates.SoloImportant : DiaryPipelineTemplates.SoloDefault;
        }

        private static DiaryPromptPlan EmptyPlan()
        {
            return new DiaryPromptPlan
            {
                povRole = string.Empty,
                templateKey = DiaryPipelineTemplates.SoloDefault,
                forcedModelName = string.Empty,
                systemPrompt = string.Empty,
                userPrompt = string.Empty,
                responseRules = new DiaryResponseRules()
            };
        }

        private static string FinalInstructionFor(DiaryPromptRequest request, DiaryTemplatePolicy template)
        {
            string instruction = request != null
                && !string.IsNullOrWhiteSpace(request.priorInitiatorEntry)
                && string.Equals(request.povRole, DiaryPipelineRoles.Recipient, StringComparison.OrdinalIgnoreCase)
                ? template.recipientFinalInstruction
                : template.finalInstruction;

            if (template.appendDirectSpeechInstruction && !string.IsNullOrWhiteSpace(request?.directSpeechInstruction))
            {
                instruction = AppendInstructionText(instruction, request.directSpeechInstruction);
            }

            return instruction;
        }

        private static PromptValues ProjectValues(DiaryTemplatePolicy template, DiaryEventPayload payload, DiaryPromptRequest request)
        {
            DiaryPovPayload pov = payload.Pov(request.povRole);
            DiaryPovPayload other = OtherPov(payload, request.povRole);
            string victimRole = DiaryContextFields.Value(payload.gameContext, "death_victim_role");
            string victimName = DiaryContextFields.Value(payload.gameContext, "death_victim");
            string arrivalPawnName = DiaryContextFields.Value(payload.gameContext, "arrival_pawn");

            return new PromptValues
            {
                eventNoun = payload.eventNoun,
                povName = pov?.name,
                povRole = RoleLabel(request.povRole),
                otherName = other?.name,
                povText = pov?.rawText,
                neutralText = payload.neutralText,
                instruction = payload.instruction,
                pawnSummary = pov?.pawnSummary,
                persona = request.personaRule,
                eventPrompt = request.policy?.group?.eventPrompt,
                eventEnhancement = request.policy?.group?.eventEnhancement,
                promptEnchantment = request.promptEnchantment,
                includePromptEnchantment = template.includePromptEnchantment,
                setting = pov?.surroundings,
                tone = request.policy?.group?.tone,
                relationship = pov?.continuity,
                lastOpener = pov?.lastOpener,
                weapon = pov?.weapon,
                initiatorEntry = request.priorInitiatorEntry,
                deathVictim = string.IsNullOrWhiteSpace(victimName) ? NameForContextRole(payload, victimRole) : victimName,
                deathFacts = BuildDeathFacts(payload.gameContext),
                deathPawnSummary = PawnSummaryForContextRole(payload, victimRole),
                deathSetting = SurroundingsForContextRole(payload, victimRole),
                arrivalPawn = string.IsNullOrWhiteSpace(arrivalPawnName) ? payload.initiator?.name : arrivalPawnName,
                arrivalFacts = BuildArrivalFacts(payload.gameContext),
                entryText = request.entryText,
                gameContext = payload.gameContext
            };
        }

        private static List<PromptAssemblerField> ToAssemblerFields(List<DiaryPromptFieldPolicy> fields)
        {
            List<PromptAssemblerField> result = new List<PromptAssemblerField>();
            if (fields == null)
            {
                return result;
            }

            for (int i = 0; i < fields.Count; i++)
            {
                DiaryPromptFieldPolicy field = fields[i];
                if (field == null)
                {
                    continue;
                }

                result.Add(new PromptAssemblerField
                {
                    label = field.label,
                    source = field.source,
                    contextKey = field.contextKey,
                    enabled = field.enabled
                });
            }

            return result;
        }

        private static DiaryPovPayload OtherPov(DiaryEventPayload payload, string role)
        {
            if (payload == null || payload.solo)
            {
                return null;
            }

            return string.Equals(role, DiaryPipelineRoles.Recipient, StringComparison.OrdinalIgnoreCase)
                ? payload.initiator
                : payload.recipient;
        }

        private static string RoleLabel(string role)
        {
            if (string.Equals(role, DiaryPipelineRoles.Recipient, StringComparison.OrdinalIgnoreCase))
            {
                return DiaryPipelineRoles.Recipient;
            }

            if (string.Equals(role, DiaryPipelineRoles.Neutral, StringComparison.OrdinalIgnoreCase))
            {
                return DiaryPipelineRoles.Neutral;
            }

            return DiaryPipelineRoles.Initiator;
        }

        private static string NameForContextRole(DiaryEventPayload payload, string role)
        {
            return string.Equals(role, DiaryPipelineRoles.Recipient, StringComparison.OrdinalIgnoreCase)
                ? payload.recipient?.name
                : payload.initiator?.name;
        }

        private static string PawnSummaryForContextRole(DiaryEventPayload payload, string role)
        {
            return string.Equals(role, DiaryPipelineRoles.Recipient, StringComparison.OrdinalIgnoreCase)
                ? payload.recipient?.pawnSummary
                : payload.initiator?.pawnSummary;
        }

        private static string SurroundingsForContextRole(DiaryEventPayload payload, string role)
        {
            return string.Equals(role, DiaryPipelineRoles.Recipient, StringComparison.OrdinalIgnoreCase)
                ? payload.recipient?.surroundings
                : payload.initiator?.surroundings;
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
            AddContextFact(parts, context, "childhood_backstory", "childhood");
            AddContextFact(parts, context, "childhood_backstory_description", "childhood description");
            AddContextFact(parts, context, "childhood_backstory_effects", "childhood effects");
            AddContextFact(parts, context, "adulthood_backstory", "adulthood");
            AddContextFact(parts, context, "adulthood_backstory_description", "adulthood description");
            AddContextFact(parts, context, "adulthood_backstory_effects", "adulthood effects");
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

        private static bool HasContext(DiaryEventPayload payload, string marker)
        {
            return payload != null && DiaryContextFields.HasMarker(payload.gameContext, marker);
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
    }
}
