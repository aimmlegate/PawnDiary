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
    internal static class DiaryPromptPlanner
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
            PromptContextSelectionResult contextSelection = PromptContextSelector.Select(
                templateKey,
                template.fields,
                values,
                payload.domain,
                payload.gameContext,
                request.contextDetailLevel,
                request.contextBudgets);

            // Inline culture annotation (MEMORY_SYSTEM_REDESIGN_PLAN §4.3): runs AFTER field
            // selection, BEFORE assembly, on the fields that survived — never on system
            // instructions, past-memory text, or prior entries (the XML scannable-source
            // allowlist excludes them structurally).
            List<PromptAssemblerField> assemblerFields = ToAssemblerFields(contextSelection.fields);
            CultureAnnotationPlan annotationPlan = PlanCultureAnnotations(
                request, payload, policy, assemblerFields, values);
            ApplyCultureAnnotations(assemblerFields, annotationPlan);

            string userPrompt = PromptAssembler.RenderUserPrompt(
                assemblerFields,
                values,
                finalInstruction);

            string systemPrompt = AppendOutputLanguageDirective(
                PromptAssembler.ComposeSystem(
                    template.systemPrompt,
                    request.personaVoiceBlock,
                    template.includePersona),
                request.outputLanguageDirective);

            int responseMaxTokens = request.maxTokens > 0 ? request.maxTokens : template.maxTokens;
            DiaryPromptPlan plan = new DiaryPromptPlan
            {
                eventId = payload.eventId,
                povRole = request.povRole,
                templateKey = templateKey,
                forcedModelName = policy.group?.forcedModelName,
                systemPrompt = systemPrompt,
                userPrompt = userPrompt,
                debugLabel = templateKey + ":" + (request.povRole ?? string.Empty),
                contextSelectionReport = contextSelection.report,
                responseRules = DiaryResponseRules.ForRequest(payload.eventId, request.povRole,
                    request.titleRequest, responseMaxTokens)
            };
            plan.cultureAnnotationTopics.AddRange(annotationPlan.matchedTopics);
            for (int i = 0; i < annotationPlan.entries.Count; i++)
            {
                int index = annotationPlan.entries[i].fieldIndex;
                if (index >= 0 && index < assemblerFields.Count && assemblerFields[index] != null)
                {
                    plan.cultureAnnotatedSources.Add(assemblerFields[index].source ?? string.Empty);
                }
            }

            return plan;
        }

        /// <summary>
        /// Builds field views of the SELECTED fields (with their final resolved values) and runs
        /// the pure annotation planner against the writer's origin/adopted culture profiles.
        /// </summary>
        private static CultureAnnotationPlan PlanCultureAnnotations(
            DiaryPromptRequest request,
            DiaryEventPayload payload,
            DiaryPolicySnapshot policy,
            List<PromptAssemblerField> assemblerFields,
            PromptValues values)
        {
            KnowledgePolicySnapshot knowledgePolicy = policy.knowledgePolicy;
            if (knowledgePolicy == null || !knowledgePolicy.injectionEnabled
                || policy.cultureTopics == null || policy.cultureTopics.Count == 0
                || request.titleRequest)
            {
                return new CultureAnnotationPlan();
            }

            DiaryPovPayload pov = payload.Pov(request.povRole);
            CultureProfile originProfile = ResolveCultureProfile(
                pov?.originCultureDefName, policy);
            CultureProfile adoptedProfile = ResolveCultureProfile(
                pov?.adoptedCultureDefName, policy);
            if (originProfile == null && adoptedProfile == null)
            {
                return new CultureAnnotationPlan();
            }

            List<AnnotationFieldView> views = new List<AnnotationFieldView>(assemblerFields.Count);
            for (int i = 0; i < assemblerFields.Count; i++)
            {
                PromptAssemblerField field = assemblerFields[i];
                if (field == null || !field.enabled)
                {
                    continue;
                }

                views.Add(new AnnotationFieldView
                {
                    // This index intentionally targets assemblerFields, not the compacted views list.
                    // ApplyCultureAnnotations uses it after the planner returns.
                    index = i,
                    source = field.source ?? string.Empty,
                    contextKey = field.contextKey ?? string.Empty,
                    resolvedValue = PromptAssembler.ResolveFieldValue(
                        field.source, field.contextKey, values),
                    structuredContext = string.Equals(
                        field.source, "GameContext", StringComparison.OrdinalIgnoreCase)
                        ? payload.gameContext ?? string.Empty
                        : string.Empty
                });
            }

            return CultureAnnotationPlanner.Plan(
                views,
                payload.defName,
                policy.cultureTopics,
                originProfile,
                adoptedProfile,
                knowledgePolicy);
        }

        /// <summary>
        /// Profile lookup (§4.2): a KNOWN culture without an authored profile uses the fallback
        /// (Astropolitan) lens; a blank culture name yields none — no invented culture.
        /// </summary>
        private static CultureProfile ResolveCultureProfile(string cultureDefName, DiaryPolicySnapshot policy)
        {
            if (string.IsNullOrWhiteSpace(cultureDefName) || policy.cultureProfiles == null)
            {
                return null;
            }

            for (int i = 0; i < policy.cultureProfiles.Count; i++)
            {
                CultureProfile profile = policy.cultureProfiles[i];
                if (profile != null && string.Equals(profile.cultureDefName, cultureDefName.Trim(),
                    StringComparison.OrdinalIgnoreCase))
                {
                    return profile;
                }
            }

            return policy.fallbackCultureProfile;
        }

        private static void ApplyCultureAnnotations(
            List<PromptAssemblerField> fields, CultureAnnotationPlan plan)
        {
            if (plan == null || plan.entries == null)
            {
                return;
            }

            for (int i = 0; i < plan.entries.Count; i++)
            {
                CultureAnnotationPlanEntry entry = plan.entries[i];
                if (entry == null || entry.fieldIndex < 0 || entry.fieldIndex >= fields.Count
                    || fields[entry.fieldIndex] == null || string.IsNullOrWhiteSpace(entry.text))
                {
                    continue;
                }

                PromptAssemblerField field = fields[entry.fieldIndex];
                field.annotation = string.IsNullOrWhiteSpace(field.annotation)
                    ? entry.text
                    : field.annotation + " " + entry.text;
            }
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

            if (payload.arcReflection)
            {
                return DiaryPipelineTemplates.SoloArcReflection;
            }

            if (payload.beliefReflection)
            {
                return DiaryPipelineTemplates.SoloBeliefReflection;
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

        /// <summary>
        /// THE central memory-projectability decision (LORE_MEMORY_SEED_PLAN §9): a template
        /// projects memory only when it declares an enabled MemoryContext field. The recall
        /// applier gates on this for the finally chosen template so recall metadata is never
        /// bumped for a page (neutral death/arrival, title) that cannot render the memory.
        /// </summary>
        public static bool ProjectsMemoryContext(DiaryTemplatePolicy template)
        {
            if (template?.fields == null)
            {
                return false;
            }

            for (int i = 0; i < template.fields.Count; i++)
            {
                DiaryPromptFieldPolicy field = template.fields[i];
                if (field != null && field.enabled
                    && string.Equals(field.source, MemoryContextPrompt.Source, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Puts the already-localized output-language sentence on the LAST line of the system prompt.
        /// Small instruction-following models weight the final instruction most, and without it they
        /// guess the output language from the prompt's own wording — which is how a Russian build ends
        /// up with English diary pages. Applied to EVERY template key including <c>Title</c>, so a page
        /// and its title never disagree about language.
        ///
        /// A blank directive returns the composed prompt untouched (no dangling newline), so a game
        /// with no active language or the XML toggle off produces byte-identical prompts to before.
        /// </summary>
        private static string AppendOutputLanguageDirective(string systemPrompt, string directive)
        {
            if (string.IsNullOrWhiteSpace(directive))
            {
                return systemPrompt;
            }

            string trimmedDirective = directive.Trim();
            if (string.IsNullOrWhiteSpace(systemPrompt))
            {
                return trimmedDirective;
            }

            return systemPrompt.TrimEnd() + "\n" + trimmedDirective;
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
            string projectedGameContext = OdysseyContextFormatter.ProjectPairRoleForPov(
                payload.gameContext,
                request.povRole);
            projectedGameContext = PollutionContextFormatter.ProjectForDetail(
                projectedGameContext,
                DetailToken(request.contextDetailLevel));
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
                narrativeContext = NarrativeContextPrompt.Compose(
                    pov?.narrativeContext,
                    request.policy?.narrativeContextInstruction),
                memoryContext = MemoryContextPrompt.Compose(
                    pov?.memoryContext,
                    request.policy?.memoryContextInstruction),
                beliefContext = BeliefContextPrompt.Compose(
                    pov?.beliefContext,
                    DetailToken(request.contextDetailLevel),
                    request.policy?.beliefPolicy,
                    request.policy?.beliefContextInstruction),
                lastOpener = pov?.lastOpener,
                // For one-sentence entries the previous ending IS the last opener; sending the same
                // sentence twice wastes tokens and models repetition. Only the per-request
                // projection drops the duplicate — the saved event keeps both snapshots untouched.
                previousEntryEnding = PromptRedundancyPolicy.SameNormalizedText(pov?.lastOpener, pov?.previousEntryEnding)
                    ? string.Empty
                    : pov?.previousEntryEnding,
                weapon = pov?.weapon,
                initiatorEntry = request.priorInitiatorEntry,
                deathVictim = string.IsNullOrWhiteSpace(victimName) ? NameForContextRole(payload, victimRole) : victimName,
                deathFacts = BuildDeathFacts(payload.gameContext),
                deathPawnSummary = PawnSummaryForContextRole(payload, victimRole),
                deathSetting = SurroundingsForContextRole(payload, victimRole),
                arrivalPawn = string.IsNullOrWhiteSpace(arrivalPawnName) ? payload.initiator?.name : arrivalPawnName,
                arrivalFacts = BuildArrivalFacts(payload.gameContext),
                entryText = request.entryText,
                gameContext = projectedGameContext
            };
        }

        private static string DetailToken(PromptContextDetailLevel level)
        {
            switch (PromptContextSelector.Normalize(level))
            {
                case PromptContextDetailLevel.Balanced:
                    return NarrativeDetailLevelTokens.Balanced;
                case PromptContextDetailLevel.Compact:
                    return NarrativeDetailLevelTokens.Compact;
                default:
                    return NarrativeDetailLevelTokens.Full;
            }
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
            AddContextFact(parts, context, "persona_milestone", "persona milestone");
            AddContextFact(parts, context, "persona_weapon_name", "persona weapon");
            AddContextFact(parts, context, "bond_previous_state", "previous bond state");
            AddContextFact(parts, context, "bond_new_state", "new bond state");
            AddContextFact(parts, context, "bond_end_cause", "bond ending cause");
            AddContextFact(parts, context, "persona_trait_1", "persona trait");
            AddContextFact(parts, context, "persona_trait_2", "persona trait");
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
