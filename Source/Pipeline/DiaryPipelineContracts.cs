// Typed contracts for the diary generation pipeline.
//
// These classes are the explicit boundary between messy RimWorld adapters and testable pure logic.
// They intentionally contain only primitive values, strings, booleans, and lists. Do not add Pawn,
// Def, Map, Verse, UnityEngine, DefDatabase, Translate(), RNG, IO, or settings reads here.
using System.Collections.Generic;

namespace PawnDiary
{
    /// <summary>
    /// Stable role names used by the pure pipeline contracts. They mirror DiaryEvent's persisted role
    /// strings without making the pure layer depend on the save model.
    /// </summary>
    internal static class DiaryPipelineRoles
    {
        public const string Initiator = "initiator";
        public const string Recipient = "recipient";
        public const string Neutral = "neutral";
    }

    /// <summary>
    /// Stable prompt-template keys understood by the pure planner. The impure XML adapter maps these
    /// names to <c>DiaryPromptTemplateDef</c> values; the planner only treats them as strings.
    /// </summary>
    internal static class DiaryPipelineTemplates
    {
        public const string PairDefault = "PairDefault";
        public const string PairImportant = "PairImportant";
        public const string PairCombat = "PairCombat";
        public const string PairBatched = "PairBatched";
        public const string SoloDefault = "SoloDefault";
        public const string SoloImportant = "SoloImportant";
        public const string SoloInternalState = "SoloInternalState";
        public const string SoloBatched = "SoloBatched";
        public const string SoloDayReflection = "SoloDayReflection";
        public const string SoloQuadrumReflection = "SoloQuadrumReflection";
        public const string SoloArcReflection = "SoloArcReflection";
        public const string DeathDescription = "DeathDescription";
        public const string ArrivalDescription = "ArrivalDescription";
        public const string Title = "Title";
    }

    /// <summary>
    /// Plain snapshot of one point of view inside a recorded diary event.
    /// </summary>
    internal class DiaryPovPayload
    {
        public string role;
        public string pawnId;
        public string name;
        public string rawText;
        public string generatedText;
        public string pawnSummary;
        public string surroundings;
        public string continuity;
        // Optional cross-event facts selected and frozen at the source event time. Empty is the normal
        // pre-N1/no-provider path and causes the XML prompt field to disappear entirely.
        public string narrativeContext;
        // Optional associative-memory recall frozen at event capture time. Empty means no memory
        // surfaced; the prompt field disappears entirely (zero token cost).
        public string memoryContext;
        public string lastOpener;
        public string previousEntryEnding;
        public string weapon;
        public bool generationAllowed = true;
        public string skipReason;
    }

    /// <summary>
    /// Display-only facts captured at event time or derived by the adapter for the UI layer. Pure
    /// prompt code may carry these through response rules, but it must not ask Unity to render them.
    /// </summary>
    internal class DiaryDisplayPayload
    {
        public string colorCue;
        public bool important = true;
    }

    /// <summary>
    /// Plain snapshot of one saved diary event. This is the contract the pure prompt planner reads.
    /// Existing code currently projects this from DiaryEvent; future event listeners can build it
    /// before persistence once the capture layer is split further.
    /// </summary>
    internal class DiaryEventPayload
    {
        public string eventId;
        public int tick;
        public string date;
        public string defName;
        public string label;
        public string eventNoun;
        public string domain;
        public bool solo;
        public string gameContext;
        public string instruction;
        public string neutralText;
        // Anti-repetition reroll counter copied from the saved event. 0 means the original seeded
        // picks (tone etc.); higher values salt the deterministic variant seeds. Pure code only
        // carries this through; the adapter owns how it is derived.
        public int variantRerolls;
        public string colonyName;
        public bool hasDeathDescription;
        public bool hasArrivalDescription;
        public bool dayReflection;
        public bool quadrumReflection;
        public bool arcReflection;
        public bool supportsDirectSpeechInstruction;
        public DiaryPovPayload initiator = new DiaryPovPayload { role = DiaryPipelineRoles.Initiator };
        public DiaryPovPayload recipient = new DiaryPovPayload { role = DiaryPipelineRoles.Recipient };
        public DiaryDisplayPayload display = new DiaryDisplayPayload();

        public DiaryPovPayload Pov(string role)
        {
            if (RoleEquals(role, DiaryPipelineRoles.Recipient))
            {
                return recipient;
            }

            if (RoleEquals(role, DiaryPipelineRoles.Neutral))
            {
                return new DiaryPovPayload
                {
                    role = DiaryPipelineRoles.Neutral,
                    name = colonyName,
                    rawText = neutralText,
                    generatedText = string.Empty,
                    pawnSummary = initiator?.pawnSummary,
                    surroundings = initiator?.surroundings,
                    continuity = "none",
                    lastOpener = string.Empty,
                    previousEntryEnding = string.Empty,
                    weapon = initiator?.weapon,
                    generationAllowed = true
                };
            }

            return initiator;
        }

        public static bool RoleEquals(string left, string right)
        {
            return string.Equals(left, right, System.StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Plain copy of one XML prompt field. The adapter copies Def fields into this class so prompt
    /// planning does not hold DiaryPromptFieldDef references.
    /// </summary>
    internal class DiaryPromptFieldPolicy
    {
        public bool enabled = true;
        public string label;
        public string source;
        public string contextKey;
    }

    /// <summary>
    /// Plain copy of one XML prompt template plus its code fallback values.
    /// </summary>
    internal class DiaryTemplatePolicy
    {
        public string templateKey;
        public string systemPrompt;
        public string finalInstruction;
        public string recipientFinalInstruction;
        public bool includePromptEnchantment = true;
        public bool includePersona = true;
        public bool appendDirectSpeechInstruction = true;
        public int maxTokens;
        public List<DiaryPromptFieldPolicy> fields = new List<DiaryPromptFieldPolicy>();
    }

    /// <summary>
    /// Plain copy of the event group policy relevant to prompt selection and display hints.
    /// </summary>
    internal class DiaryGroupPolicy
    {
        public string defName;
        public string domain;
        public string classifierKey;
        public string eventPromptKey;
        public string eventPrompt;
        public string eventEnhancement;
        public string forcedModelName;
        public bool important = true;
        public bool combat;
        public string colorCue;
        public string tone;
    }

    /// <summary>
    /// Complete prompt-policy snapshot for one planning pass. XML/Def lookups happen before this
    /// object is built; pure code only receives this copy.
    /// </summary>
    internal class DiaryPolicySnapshot
    {
        public DiaryGroupPolicy group = new DiaryGroupPolicy();
        public List<DiaryTemplatePolicy> templates = new List<DiaryTemplatePolicy>();
        // Copied from DiaryNarrativeContinuityDef on the main thread. The pure planner uses these
        // strings only to render an already-frozen factual field; it never reads a live Def.
        public string narrativeContextFieldLabel = "narrative context";
        public string narrativeContextInstruction = string.Empty;
        // Copied from DiaryMemoryTuningDef on the main thread. The pure planner uses this string
        // only to prefix the already-frozen memory recall; it never reads a live Def.
        public string memoryContextInstruction = string.Empty;

        public DiaryTemplatePolicy Template(string templateKey)
        {
            for (int i = 0; i < templates.Count; i++)
            {
                DiaryTemplatePolicy template = templates[i];
                if (template != null && string.Equals(template.templateKey, templateKey, System.StringComparison.OrdinalIgnoreCase))
                {
                    return template;
                }
            }

            return new DiaryTemplatePolicy { templateKey = templateKey };
        }
    }

    /// <summary>
    /// Input to the pure prompt planner. Impure callers resolve writing-style text, prompt enchantments,
    /// localized direct-speech snippets, and XML policy snapshots before constructing this request.
    /// </summary>
    internal class DiaryPromptRequest
    {
        public DiaryEventPayload payload;
        public DiaryPolicySnapshot policy;
        public string povRole;
        public bool titleRequest;
        public string personaRule;
        public string personaVoiceBlock;
        public string promptEnchantment;
        public string priorInitiatorEntry;
        public string entryText;
        public string directSpeechInstruction;
        public PromptContextDetailLevel contextDetailLevel = PromptContextDetailLevel.Full;
        // Optional XML-backed Balanced/Compact budgets; null makes the selector use its code defaults.
        public PromptContextBudgets contextBudgets;
        public int maxTokens;
    }

    /// <summary>
    /// Plain rules captured when a prompt is queued and reused after the model responds. Response
    /// parsing and postprocessing must not reread game state.
    /// </summary>
    internal class DiaryResponseRules
    {
        public string eventId;
        public string targetRole;
        public bool isTitle;
        public int maxTokens;
        public bool trimIncompleteSentence = true;

        public static DiaryResponseRules ForRequest(string eventId, string targetRole, bool isTitle, int maxTokens)
        {
            return new DiaryResponseRules
            {
                eventId = eventId,
                targetRole = targetRole,
                isTitle = isTitle,
                maxTokens = maxTokens,
                trimIncompleteSentence = !isTitle
            };
        }
    }

    /// <summary>
    /// Pure prompt-planning result. The impure queueing layer stamps the user prompt on DiaryEvent,
    /// records endpoint metadata, and passes the prompts to LlmClient.
    /// </summary>
    internal class DiaryPromptPlan
    {
        public string eventId;
        public string povRole;
        public string templateKey;
        public string forcedModelName;
        public string systemPrompt;
        public string userPrompt;
        public string debugLabel;
        public PromptContextSelectionReport contextSelectionReport;
        public DiaryResponseRules responseRules = new DiaryResponseRules();
    }

    /// <summary>
    /// Pure response-postprocessing result. The persistence adapter applies this to DiaryEvent.
    /// </summary>
    internal class DiaryResponsePlan
    {
        public string eventId;
        public string povRole;
        public bool success;
        public string generatedText;
        public string rawVisibleResponse;
        public string error;
        public string titleText;
        public DiaryResponseRules responseRules;
    }
}
