// All mod settings (connection, generation options, and per-group enable/instruction overrides)
// plus value clamping and save/load. ExposeData persists them — see CSHARP-NOTES.md ("IExposable").
// The group catalog itself now lives in XML Defs (see InteractionGroups.cs); this file only
// stores the player's per-group overrides, keyed by group defName.
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

        // Per interaction-group settings, keyed by InteractionGroup.defName.
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

        // Default comes from the DiaryPromptDef XML (editable without recompiling). If the Def
        // isn't loaded yet during early startup, the field initializer in DiaryPromptDef
        // provides the same hardcoded text as a fallback.
        public static string DefaultSystemPrompt => DiaryPrompts.Current.systemPrompt;

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

            // Classify only returns null if the group catalog (XML Defs) failed to load; treat
            // that as "not recorded" rather than crashing on every interaction.
            DiaryInteractionGroupDef group = InteractionGroups.Classify(interactionDef);
            return group != null && IsGroupEnabled(group.defName);
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

            DiaryInteractionGroupDef group = InteractionGroups.ClassifyMentalState(stateDef);
            return group != null && IsGroupEnabled(group.defName);
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

            return InteractionGroups.ByKey(groupKey)?.defaultEnabled ?? false;
        }

        public void SetGroupEnabled(string groupKey, bool enabled)
        {
            EnsureGroupDictionaries();
            groupEnabled[groupKey] = enabled;
        }

        public string InstructionForGroup(DiaryInteractionGroupDef group)
        {
            if (group == null)
            {
                return string.Empty;
            }

            EnsureGroupDictionaries();

            string instruction;
            if (groupInstructions.TryGetValue(group.defName, out instruction) && !string.IsNullOrWhiteSpace(instruction))
            {
                return instruction.Trim();
            }

            return group.instruction;
        }

        public string EditableInstructionForGroup(DiaryInteractionGroupDef group)
        {
            if (group == null)
            {
                return string.Empty;
            }

            EnsureGroupDictionaries();

            string instruction;
            if (groupInstructions.TryGetValue(group.defName, out instruction))
            {
                return instruction;
            }

            return group.instruction;
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
