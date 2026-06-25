// Impure adapters for the typed diary pipeline.
//
// This is the boundary where existing save models, XML Def helpers, localization, and the occasional
// RimWorld DefDatabase fallback are copied into plain pipeline contracts. Keep those dependencies
// here; do not let pure planners accept DiaryEvent, Def, Pawn, or Verse objects.
using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace PawnDiary
{
    /// <summary>
    /// Converts current runtime/save objects into pure pipeline contracts.
    /// </summary>
    public static class DiaryPipelineAdapters
    {
        public static DiaryPromptRequest BuildPromptRequest(
            DiaryEvent diaryEvent,
            string povRole,
            string personaRule,
            string promptEnchantment,
            string humorCue,
            string priorInitiatorEntry,
            string entryText,
            bool titleRequest,
            int maxTokens)
        {
            DiaryEventPayload payload = ToPayload(diaryEvent);
            string normalizedRole = string.IsNullOrWhiteSpace(povRole) ? DiaryPipelineRoles.Initiator : povRole;
            return new DiaryPromptRequest
            {
                payload = payload,
                policy = PolicyFor(payload),
                povRole = normalizedRole,
                titleRequest = titleRequest,
                personaRule = personaRule,
                // The per-entry humor cue rides inside the persona voice block rather than on its own
                // request field, so no planner/contract change is needed and it is automatically
                // suppressed when a template opts out of persona text (neutral death/arrival/title).
                personaVoiceBlock = CombinedVoiceBlock(PersonaVoiceBlock(personaRule), HumorVoiceBlock(humorCue)),
                promptEnchantment = promptEnchantment,
                priorInitiatorEntry = priorInitiatorEntry,
                entryText = entryText,
                directSpeechInstruction = titleRequest ? string.Empty : DirectSpeechInstructionFor(diaryEvent, normalizedRole),
                maxTokens = maxTokens
            };
        }

        public static DiaryEventPayload ToPayload(DiaryEvent diaryEvent)
        {
            if (diaryEvent == null)
            {
                return new DiaryEventPayload
                {
                    eventNoun = "PawnDiary.Prompt.SocialEvent".Translate().Resolve(),
                    colonyName = "PawnDiary.Prompt.Colony".Translate().Resolve()
                };
            }

            DiaryEventPayload payload = new DiaryEventPayload
            {
                eventId = diaryEvent.eventId,
                tick = diaryEvent.tick,
                date = diaryEvent.date,
                defName = diaryEvent.interactionDefName,
                label = diaryEvent.interactionLabel,
                eventNoun = EventNoun(diaryEvent),
                domain = DomainFor(diaryEvent.gameContext),
                solo = diaryEvent.solo,
                gameContext = diaryEvent.gameContext,
                instruction = diaryEvent.instruction,
                neutralText = diaryEvent.neutralText,
                colonyName = "PawnDiary.Prompt.Colony".Translate().Resolve(),
                hasDeathDescription = diaryEvent.HasDeathDescription(),
                hasArrivalDescription = diaryEvent.HasArrivalDescription(),
                dayReflection = diaryEvent.IsDayReflection(),
                supportsDirectSpeechInstruction = IsInteractionPrompt(diaryEvent),
                initiator = PovFor(diaryEvent, DiaryPipelineRoles.Initiator),
                recipient = PovFor(diaryEvent, DiaryPipelineRoles.Recipient),
                display = new DiaryDisplayPayload
                {
                    colorCue = DiaryEvent.ResolveColorCue(diaryEvent.interactionDefName, diaryEvent.gameContext),
                    important = diaryEvent.IsImportant()
                }
            };

            return payload;
        }

        public static DiaryPolicySnapshot PolicyFor(DiaryEventPayload payload)
        {
            string classifierKey = ClassifierKeyForPayload(payload);
            string eventPromptKey = EventPromptKeyForPayload(payload);
            DiaryInteractionGroupDef group = GroupForPayload(payload, classifierKey);
            DiaryEventPromptDef eventPrompt = DiaryEventPrompts.ForKey(eventPromptKey);
            string eventPromptText = PawnDiaryMod.Settings == null
                ? eventPrompt?.prompt
                : PawnDiaryMod.Settings.eventPromptOverrides.Effective(eventPromptKey, eventPrompt?.prompt);
            string eventEnhancementText = PawnDiaryMod.Settings == null
                ? eventPrompt?.enhancement
                : PawnDiaryMod.Settings.eventEnhancementOverrides.Effective(eventPromptKey, eventPrompt?.enhancement);
            string forcedModelName = PawnDiaryMod.Settings == null
                ? eventPrompt?.forcedModel
                : PawnDiaryMod.Settings.eventForcedModelOverrides.Effective(eventPromptKey, eventPrompt?.forcedModel);
            DiaryPolicySnapshot snapshot = new DiaryPolicySnapshot
            {
                group = new DiaryGroupPolicy
                {
                    defName = group?.defName,
                    domain = payload?.domain,
                    classifierKey = classifierKey,
                    eventPromptKey = eventPromptKey,
                    eventPrompt = eventPromptText,
                    eventEnhancement = eventEnhancementText,
                    forcedModelName = forcedModelName,
                    important = payload?.display == null || payload.display.important,
                    combat = GroupCombat(payload, group),
                    colorCue = payload?.display?.colorCue,
                    tone = ToneFor(group, payload?.eventId)
                }
            };

            AddTemplate(snapshot, DiaryPipelineTemplates.PairDefault);
            AddTemplate(snapshot, DiaryPipelineTemplates.PairImportant);
            AddTemplate(snapshot, DiaryPipelineTemplates.PairCombat);
            AddTemplate(snapshot, DiaryPipelineTemplates.PairBatched);
            AddTemplate(snapshot, DiaryPipelineTemplates.SoloDefault);
            AddTemplate(snapshot, DiaryPipelineTemplates.SoloImportant);
            AddTemplate(snapshot, DiaryPipelineTemplates.SoloInternalState);
            AddTemplate(snapshot, DiaryPipelineTemplates.SoloBatched);
            AddTemplate(snapshot, DiaryPipelineTemplates.SoloDayReflection);
            AddTemplate(snapshot, DiaryPipelineTemplates.DeathDescription);
            AddTemplate(snapshot, DiaryPipelineTemplates.ArrivalDescription);
            AddTemplate(snapshot, DiaryPipelineTemplates.Title);
            return snapshot;
        }

        private static DiaryPovPayload PovFor(DiaryEvent diaryEvent, string role)
        {
            bool recipient = string.Equals(role, DiaryPipelineRoles.Recipient, StringComparison.OrdinalIgnoreCase);
            return new DiaryPovPayload
            {
                role = role,
                pawnId = recipient ? diaryEvent.recipientPawnId : diaryEvent.initiatorPawnId,
                name = recipient ? diaryEvent.recipientName : diaryEvent.initiatorName,
                rawText = diaryEvent.TextForRole(role),
                generatedText = recipient ? diaryEvent.recipientGeneratedText : diaryEvent.initiatorGeneratedText,
                pawnSummary = recipient ? diaryEvent.recipientPawnSummary : diaryEvent.initiatorPawnSummary,
                surroundings = diaryEvent.SurroundingsForRole(role),
                continuity = diaryEvent.ContinuityForRole(role),
                lastOpener = diaryEvent.LastOpenerForRole(role),
                weapon = recipient ? diaryEvent.recipientWeapon : diaryEvent.initiatorWeapon,
                generationAllowed = !diaryEvent.IsSkipped(role),
                skipReason = string.Empty
            };
        }

        private static void AddTemplate(DiaryPolicySnapshot snapshot, string templateKey)
        {
            DiaryPromptTemplateDef template = DiaryPromptTemplates.ForKey(templateKey);
            snapshot.templates.Add(new DiaryTemplatePolicy
            {
                templateKey = templateKey,
                systemPrompt = DiaryPromptTemplates.SystemPromptFor(templateKey),
                finalInstruction = DiaryPromptTemplates.FinalInstructionFor(templateKey),
                recipientFinalInstruction = DiaryPromptTemplates.RecipientFinalInstruction(templateKey),
                includePromptEnchantment = template.includePromptEnchantment,
                includePersona = template.includePersona,
                appendDirectSpeechInstruction = template.appendDirectSpeechInstruction,
                fields = CopyFields(DiaryPromptTemplates.FieldsFor(templateKey))
            });
        }

        private static List<DiaryPromptFieldPolicy> CopyFields(List<DiaryPromptFieldDef> fields)
        {
            List<DiaryPromptFieldPolicy> result = new List<DiaryPromptFieldPolicy>();
            if (fields == null)
            {
                return result;
            }

            for (int i = 0; i < fields.Count; i++)
            {
                DiaryPromptFieldDef field = fields[i];
                if (field == null)
                {
                    continue;
                }

                result.Add(new DiaryPromptFieldPolicy
                {
                    enabled = field.enabled,
                    label = field.label,
                    source = field.source,
                    contextKey = field.contextKey
                });
            }

            return result;
        }

        private static string PersonaVoiceBlock(string personaRule)
        {
            if (string.IsNullOrWhiteSpace(personaRule))
            {
                return string.Empty;
            }

            return "PawnDiary.Prompt.PersonaVoice".Translate(personaRule.Trim()).Resolve();
        }

        // Wraps one chosen humor cue (a structural sentence-shape license) in the same kind of
        // model-facing frame as the writing style. Blank cue => no block, so the feature is a no-op
        // on the ~90% of entries that do not roll a cue. Main thread only: .Translate() is not
        // thread-safe.
        private static string HumorVoiceBlock(string humorCue)
        {
            if (string.IsNullOrWhiteSpace(humorCue))
            {
                return string.Empty;
            }

            return "PawnDiary.Prompt.HumorVoice".Translate(humorCue.Trim()).Resolve();
        }

        // Joins the writing-style block and the humor block with a blank line between them. If either
        // is empty, the other stands alone; if both are empty, the whole voice block is empty (which
        // is the correct shape for neutral death/arrival/title prompts).
        private static string CombinedVoiceBlock(string personaVoiceBlock, string humorVoiceBlock)
        {
            bool hasPersona = !string.IsNullOrWhiteSpace(personaVoiceBlock);
            bool hasHumor = !string.IsNullOrWhiteSpace(humorVoiceBlock);
            if (hasPersona && hasHumor)
            {
                return personaVoiceBlock + "\n\n" + humorVoiceBlock;
            }

            if (hasPersona)
            {
                return personaVoiceBlock;
            }

            return hasHumor ? humorVoiceBlock : string.Empty;
        }

        private static string EventNoun(DiaryEvent diaryEvent)
        {
            string label = DiaryLineCleaner.CleanLine(diaryEvent.interactionLabel);
            if (string.IsNullOrWhiteSpace(label))
            {
                return "PawnDiary.Prompt.SocialEvent".Translate().Resolve();
            }

            return label.ToLowerInvariant();
        }

        private static bool GroupCombat(DiaryEventPayload payload, DiaryInteractionGroupDef group)
        {
            if (payload == null)
            {
                return false;
            }

            if (string.Equals(payload.domain, GroupDomain.MentalState.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return group != null && group.combat;
        }

        // One tone wording for the group, or empty. When the group has a `tones` variant pool, a
        // wording is picked deterministically by the event id so the same entry keeps the same tone
        // across save/load and regeneration; the singular `tone` is the fallback. Seeding keeps the
        // otherwise-impure pick out of the pure planner (RNG-free by contract).
        private static string ToneFor(DiaryInteractionGroupDef group, string seed)
        {
            if (group == null)
            {
                return string.Empty;
            }

            return PromptVariants.Pick(group.tones, group.tone, PromptVariants.HashSeed(seed));
        }

        private static DiaryInteractionGroupDef GroupForPayload(DiaryEventPayload payload, string classifierKey)
        {
            if (payload == null)
            {
                return null;
            }

            string domainName = payload.domain ?? string.Empty;
            GroupDomain domain;
            if (!Enum.TryParse(domainName, out domain))
            {
                domain = GroupDomain.Interaction;
            }

            return InteractionGroups.ClassifyDefName(domain, classifierKey);
        }

        private static string ClassifierKeyForPayload(DiaryEventPayload payload)
        {
            return payload == null
                ? string.Empty
                : DiaryEventDomainClassifier.GroupClassifierKey(
                    payload.domain, payload.gameContext, payload.defName);
        }

        private static string EventPromptKeyForPayload(DiaryEventPayload payload)
        {
            if (payload == null)
            {
                return DiaryEventDomainClassifier.Interaction;
            }

            if (payload.hasDeathDescription)
            {
                return "Death";
            }

            if (payload.hasArrivalDescription)
            {
                return "Arrival";
            }

            if (payload.dayReflection)
            {
                return "DayReflection";
            }

            return string.IsNullOrWhiteSpace(payload.domain)
                ? DiaryEventDomainClassifier.Interaction
                : payload.domain;
        }

        private static string DomainFor(string context)
        {
            return DiaryEventDomainClassifier.DomainForContext(context);
        }

        private static string DirectSpeechInstructionFor(DiaryEvent diaryEvent, string povRole)
        {
            if (diaryEvent == null || !IsInteractionPrompt(diaryEvent))
            {
                return string.Empty;
            }

            if (diaryEvent.solo)
            {
                return "PawnDiary.Prompt.SoloInteractionDirectSpeechInstruction"
                    .Translate(diaryEvent.initiatorName)
                    .Resolve();
            }

            bool isInitiator = string.Equals(povRole, DiaryPipelineRoles.Initiator, StringComparison.OrdinalIgnoreCase);
            bool isRecipient = string.Equals(povRole, DiaryPipelineRoles.Recipient, StringComparison.OrdinalIgnoreCase);
            // The persisted model no longer resolves localized names: pick the saved
            // initiator/recipient name here and fall back to the localized colony label for neutral.
            string povName = isInitiator ? diaryEvent.initiatorName
                : isRecipient ? diaryEvent.recipientName
                : "PawnDiary.Prompt.Colony".Translate().Resolve();
            string otherName = isInitiator ? diaryEvent.recipientName : diaryEvent.initiatorName;
            string key = isInitiator
                ? "PawnDiary.Prompt.PairDirectSpeechInstruction.Initiator"
                : "PawnDiary.Prompt.PairDirectSpeechInstruction.Recipient";
            return key.Translate(povName, otherName).Resolve();
        }

        private static bool IsInteractionPrompt(DiaryEvent diaryEvent)
        {
            if (diaryEvent == null)
            {
                return false;
            }

            if (DiaryEventDomainClassifier.HasNonInteractionSourceMarker(diaryEvent.gameContext)
                || HasContext(diaryEvent, "batch=ambient_day_note")
                || HasContext(diaryEvent, "arrival_description=")
                || HasContext(diaryEvent, "death_description=")
                || HasContext(diaryEvent, "dev_mock=")
                || HasContext(diaryEvent, "day_reflection="))
            {
                return false;
            }

            return HasContext(diaryEvent, "batch=interaction")
                || DefDatabase<InteractionDef>.GetNamedSilentFail(diaryEvent.interactionDefName) != null;
        }

        private static bool HasContext(DiaryEvent diaryEvent, string marker)
        {
            return diaryEvent != null && DiaryContextFields.HasMarker(diaryEvent.gameContext, marker);
        }
    }
}
