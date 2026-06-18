// Thin façade over the pure diary prompt pipeline. Each method projects a DiaryEvent (plus the
// already-resolved persona/enchantment strings) into a typed DiaryPromptRequest via
// DiaryPipelineAdapters, then runs the pure DiaryPromptPlanner to produce the prompt envelope sent to
// the model. All the real work — template selection, field rendering, system-prompt/persona
// composition, direct-speech rules — lives in Source/Pipeline now. This file only exists so the
// generation orchestrator (DiaryGameComponent.Generation) keeps the same small call surface it had
// before the pipeline split. See DOCUMENTATION.md.
using System;

namespace PawnDiary
{
    public static class DiaryPromptBuilder
    {
        // Prompts intentionally omit any field that is empty or "normal" (see PromptAssembler.AppendField),
        // so the model only ever sees signal — no "health: healthy", no weather indoors, etc.
        public static DiaryPromptPlan BuildSequentialInteractionPromptPlan(
            DiaryEvent diaryEvent,
            string povRole,
            string personaRule,
            string promptEnchantment,
            int maxTokens = 0)
        {
            // In a paired event the recipient writes second, so it sees the initiator's finished entry
            // as hidden context. Only the recipient POV carries that prior entry.
            string initiatorEntry = diaryEvent != null
                && !diaryEvent.solo
                && string.Equals(povRole, DiaryEvent.RecipientRole, StringComparison.OrdinalIgnoreCase)
                ? DiaryContextBuilder.CleanLine(diaryEvent.initiatorGeneratedText)
                : null;

            return BuildPromptPlan(diaryEvent, povRole, personaRule, promptEnchantment, initiatorEntry, null, false, maxTokens);
        }

        public static DiaryPromptPlan BuildInteractionPromptPlan(
            DiaryEvent diaryEvent,
            string povRole,
            string personaRule,
            string promptEnchantment,
            int maxTokens = 0)
        {
            return BuildPromptPlan(diaryEvent, povRole, personaRule, promptEnchantment, null, null, false, maxTokens);
        }

        /// <summary>
        /// Builds the neutral, persona-independent prompt used only for colonist death
        /// descriptions. It deliberately omits persona, relationship continuity, and first-person
        /// POV fields because this output is a factual death note, not a diary entry.
        /// </summary>
        public static DiaryPromptPlan BuildDeathDescriptionPromptPlan(DiaryEvent diaryEvent, int maxTokens = 0)
        {
            return BuildPromptPlan(diaryEvent, DiaryEvent.NeutralRole, string.Empty, string.Empty, null, null, false, maxTokens);
        }

        /// <summary>
        /// Builds the neutral, persona-independent prompt used for the first diary entry: how this
        /// pawn became part of the colony. Starting pawns get scenario context; later pawns get the
        /// SetFaction/join facts captured at runtime.
        /// </summary>
        public static DiaryPromptPlan BuildArrivalDescriptionPromptPlan(DiaryEvent diaryEvent, int maxTokens = 0)
        {
            return BuildPromptPlan(diaryEvent, DiaryEvent.NeutralRole, string.Empty, string.Empty, null, null, false, maxTokens);
        }

        /// <summary>
        /// Builds the user message for the title-generation follow-up call. The system
        /// prompt lives on the request and tells the model to return a title; the user message
        /// carries the diary entry to summarize. Uses the LLM-generated entry when available,
        /// else falls back to the raw game text. The title prompt is intentionally small and
        /// cheap — see <see cref="DiaryGameComponent.Generation.QueueTitleRequest"/>.
        /// </summary>
        public static DiaryPromptPlan BuildTitlePromptPlan(DiaryEvent diaryEvent, string povRole, int maxTokens = 0)
        {
            string entryText = diaryEvent == null ? string.Empty : diaryEvent.DisplayTextForRole(povRole);
            return BuildPromptPlan(diaryEvent, povRole, string.Empty, string.Empty, null, entryText, true, maxTokens);
        }

        /// <summary>
        /// Returns true when this non-neutral prompt can use a live prompt enchantment (a hediff health
        /// hint that travels with persona). Neutral chronicle/title prompts stay persona- and
        /// enchantment-free. The decision reads the SAME template the shipped prompt will use — by
        /// running the pure planner's selection — so this gate can never drift from the template that
        /// actually renders. Template selection ignores POV/persona/enchantment, so neutral
        /// placeholders are fine for this probe. Call on the main thread: building the request resolves
        /// localized strings (<c>.Translate()</c>), exactly as the real prompt build does.
        /// </summary>
        public static bool ShouldResolvePromptEnchantment(DiaryEvent diaryEvent)
        {
            DiaryPromptRequest request = DiaryPipelineAdapters.BuildPromptRequest(
                diaryEvent,
                DiaryEvent.InitiatorRole,
                string.Empty,
                string.Empty,
                null,
                null,
                false,
                0);
            string templateKey = DiaryPromptPlanner.TemplateKeyFor(request);
            return request.policy.Template(templateKey).includePromptEnchantment;
        }

        // Projects the event into a typed request (impure adapter, main thread) and runs the pure
        // planner. All prompt shaping lives in DiaryPromptPlanner / DiaryPipelineAdapters.
        private static DiaryPromptPlan BuildPromptPlan(
            DiaryEvent diaryEvent,
            string povRole,
            string personaRule,
            string promptEnchantment,
            string priorInitiatorEntry,
            string entryText,
            bool titleRequest,
            int maxTokens)
        {
            DiaryPromptRequest request = DiaryPipelineAdapters.BuildPromptRequest(
                diaryEvent,
                povRole,
                personaRule,
                promptEnchantment,
                priorInitiatorEntry,
                entryText,
                titleRequest,
                maxTokens);
            return DiaryPromptPlanner.Build(request);
        }
    }
}
