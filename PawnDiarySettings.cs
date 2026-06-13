using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnDiary
{
    public class PawnDiarySettings : ModSettings
    {
        public bool enableLlm = true;
        public string endpointUrl = DefaultEndpointUrl;
        public string modelName = DefaultModelName;
        public string apiKey = string.Empty;

        public bool sendApiKeyAsBearerToken = true;
        public int timeoutSeconds = 30;
        public int maxConcurrentRequests = 4;
        public int maxTokens = 160;
        public float temperature = 0.8f;
        public bool keepRawEntryOnFailure = true;
        public bool dualPovGeneration = true;
        public string systemPrompt = DefaultSystemPrompt;

        // Per interaction-group settings, keyed by InteractionGroup.Key.
        // groupEnabled: whether interactions in the group are recorded at all.
        // groupInstructions: optional override of the group's default diary prompt.
        public Dictionary<string, bool> groupEnabled = new Dictionary<string, bool>();
        public Dictionary<string, string> groupInstructions = new Dictionary<string, string>();

        private List<string> groupEnabledKeys;
        private List<bool> groupEnabledValues;
        private List<string> groupInstructionKeys;
        private List<string> groupInstructionValues;

        public const string DefaultEndpointUrl = "http://localhost:1234/v1";
        public const string DefaultModelName = "local-model";

        public const string DefaultSystemPrompt =
            "You are the diary-writer for a RimWorld colony. You receive structured notes about a social interaction "
            + "between colonists and write short, first-person diary entries in the voice of the colonist whose point of view is requested.\n"
            + "Rules:\n"
            + "- Write only what that colonist could plausibly know, see, or feel. Never invent events, names, places, or facts that are not in the notes.\n"
            + "- Stay in first person and in character. Reflect the colonist's traits, mood, and their opinion of the other pawn.\n"
            + "- Keep each entry to a few sentences. Be concrete and grounded in the provided context; do not moralize or summarize game mechanics.\n"
            + "- Do not use markdown, headings, or any commentary outside the diary text.\n"
            + "- When the notes specify an output format (for example labelled sections like [INITIATOR] and [RECIPIENT]), follow it exactly and output nothing else.";

        public override void ExposeData()
        {
            string legacyChatCompletionsUrl = null;

            Scribe_Values.Look(ref enableLlm, "enableLlm", true);
            Scribe_Values.Look(ref endpointUrl, "endpointUrl", DefaultEndpointUrl);
            Scribe_Values.Look(ref legacyChatCompletionsUrl, "chatCompletionsUrl", null);
            Scribe_Values.Look(ref modelName, "modelName", DefaultModelName);
            Scribe_Values.Look(ref apiKey, "apiKey", string.Empty);
            Scribe_Values.Look(ref sendApiKeyAsBearerToken, "sendApiKeyAsBearerToken", true);
            Scribe_Values.Look(ref timeoutSeconds, "timeoutSeconds", 30);
            Scribe_Values.Look(ref maxConcurrentRequests, "maxConcurrentRequests", 4);
            Scribe_Values.Look(ref maxTokens, "maxTokens", 160);
            Scribe_Values.Look(ref temperature, "temperature", 0.8f);
            Scribe_Values.Look(ref keepRawEntryOnFailure, "keepRawEntryOnFailure", true);
            Scribe_Values.Look(ref dualPovGeneration, "dualPovGeneration", true);
            Scribe_Values.Look(ref systemPrompt, "systemPrompt", DefaultSystemPrompt);
            Scribe_Collections.Look(ref groupEnabled, "interactionGroupEnabled", LookMode.Value, LookMode.Value, ref groupEnabledKeys, ref groupEnabledValues);
            Scribe_Collections.Look(ref groupInstructions, "interactionGroupInstructions", LookMode.Value, LookMode.Value, ref groupInstructionKeys, ref groupInstructionValues);

            if (!string.IsNullOrWhiteSpace(legacyChatCompletionsUrl) && endpointUrl == DefaultEndpointUrl)
            {
                endpointUrl = EndpointUtility.NormalizeBaseEndpoint(legacyChatCompletionsUrl);
            }

            ClampValues();
        }

        public void ResetConnectionDefaults()
        {
            endpointUrl = DefaultEndpointUrl;
            modelName = DefaultModelName;
            apiKey = string.Empty;
        }

        // ---- Interaction group helpers ----

        // Whether an interaction should be recorded at all (its group is enabled).
        public bool IsInteractionEnabled(InteractionDef interactionDef)
        {
            if (interactionDef == null)
            {
                return false;
            }

            return IsGroupEnabled(InteractionGroups.Classify(interactionDef).Key);
        }

        // The diary prompt instruction for an interaction (its group's override or default).
        public string InstructionFor(InteractionDef interactionDef)
        {
            if (interactionDef == null)
            {
                return string.Empty;
            }

            return InstructionForGroup(InteractionGroups.Classify(interactionDef));
        }

        // Mental-state equivalents (social fights, mental breaks).
        public bool IsMentalStateEnabled(MentalStateDef stateDef)
        {
            if (stateDef == null)
            {
                return false;
            }

            return IsGroupEnabled(InteractionGroups.ClassifyMentalState(stateDef).Key);
        }

        public string InstructionForMentalState(MentalStateDef stateDef)
        {
            if (stateDef == null)
            {
                return string.Empty;
            }

            return InstructionForGroup(InteractionGroups.ClassifyMentalState(stateDef));
        }

        public bool IsGroupEnabled(string groupKey)
        {
            EnsureGroupDictionaries();

            bool enabled;
            if (groupEnabled.TryGetValue(groupKey, out enabled))
            {
                return enabled;
            }

            return InteractionGroups.ByKey(groupKey)?.DefaultEnabled ?? false;
        }

        public void SetGroupEnabled(string groupKey, bool enabled)
        {
            EnsureGroupDictionaries();
            groupEnabled[groupKey] = enabled;
        }

        public string InstructionForGroup(InteractionGroup group)
        {
            if (group == null)
            {
                return string.Empty;
            }

            EnsureGroupDictionaries();

            string instruction;
            if (groupInstructions.TryGetValue(group.Key, out instruction) && !string.IsNullOrWhiteSpace(instruction))
            {
                return instruction.Trim();
            }

            return group.DefaultInstruction;
        }

        public string EditableInstructionForGroup(InteractionGroup group)
        {
            if (group == null)
            {
                return string.Empty;
            }

            EnsureGroupDictionaries();

            string instruction;
            if (groupInstructions.TryGetValue(group.Key, out instruction))
            {
                return instruction;
            }

            return group.DefaultInstruction;
        }

        public void SetGroupInstruction(string groupKey, string instruction)
        {
            EnsureGroupDictionaries();
            groupInstructions[groupKey] = instruction ?? string.Empty;
        }

        public void ResetGroupInstruction(string groupKey)
        {
            if (groupInstructions != null)
            {
                groupInstructions.Remove(groupKey);
            }
        }

        public void ResetAllGroups()
        {
            EnsureGroupDictionaries();
            groupEnabled.Clear();
            groupInstructions.Clear();
        }

        public void ClampValues()
        {
            enableLlm = true;
            keepRawEntryOnFailure = true;
            sendApiKeyAsBearerToken = true;

            EnsureGroupDictionaries();

            if (string.IsNullOrWhiteSpace(endpointUrl))
            {
                endpointUrl = DefaultEndpointUrl;
            }

            endpointUrl = EndpointUtility.NormalizeBaseEndpoint(endpointUrl);

            if (string.IsNullOrWhiteSpace(modelName))
            {
                modelName = DefaultModelName;
            }

            if (systemPrompt == null)
            {
                systemPrompt = string.Empty;
            }

            timeoutSeconds = Mathf.Clamp(timeoutSeconds, 5, 300);
            maxConcurrentRequests = Mathf.Clamp(maxConcurrentRequests, 1, 16);
            maxTokens = Mathf.Clamp(maxTokens, 32, 2048);
            temperature = Mathf.Clamp(temperature, 0f, 2f);
        }

        private void EnsureGroupDictionaries()
        {
            if (groupEnabled == null)
            {
                groupEnabled = new Dictionary<string, bool>();
            }

            if (groupInstructions == null)
            {
                groupInstructions = new Dictionary<string, string>();
            }
        }
    }
}
