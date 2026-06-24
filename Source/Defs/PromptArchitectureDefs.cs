// XML-backed prompt architecture. These Defs control which structured fields are sent to the
// model, how tracked map context is summarized, and how generic event trackers expose their tuning.
// The C# side still observes live game state; XML decides how that state becomes prompt context.
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace PawnDiary
{
    /// <summary>
    /// One line in a prompt template. The source token is an internal, stable key such as
    /// "PovName" or "PromptEnchantment"; label is the model-facing structured field name.
    /// </summary>
    public class DiaryPromptFieldDef
    {
        public bool enabled = true;
        public string label;
        public string source;
        public string contextKey;
    }

    /// <summary>
    /// XML template for one prompt shape: pair combat, solo internal state, title, neutral death
    /// description, and so on. Missing system/final instructions fall back to DiaryPromptDef.
    /// </summary>
    public class DiaryPromptTemplateDef : Def
    {
        public string templateKey;
        public string systemPrompt;
        public string finalInstruction;
        public string recipientFinalInstruction;
        public bool includePromptEnchantment = true;
        // When true, the pawn's writing-style rule is appended to the system prompt for this shape
        // (composed by DiaryPromptPlanner.Build via PromptAssembler.ComposeSystem). Style now governs the system prompt rather
        // than competing as one field in the user message, so first-person shapes keep this true and
        // the neutral chronicle/title shapes set it false to stay style-free.
        public bool includePersona = true;
        public bool appendDirectSpeechInstruction = true;
        public List<DiaryPromptFieldDef> fields = new List<DiaryPromptFieldDef>();
    }

    /// <summary>
    /// XML prompt policy for one broad event source such as Quest, Raid, Thought, or Work. These
    /// fields render into every first-person prompt as source-level guidance before the narrower
    /// group instruction/tone. Missing XML safely means no event-type guidance.
    /// </summary>
    public class DiaryEventPromptDef : Def
    {
        public string eventType;
        public string prompt;
        public string enhancement;
    }

    /// <summary>
    /// Lookup helper for broad event-source prompt policy. The adapter resolves this on the impure
    /// side and copies only plain strings into the pure pipeline snapshot.
    /// </summary>
    public static class DiaryEventPrompts
    {
        private static readonly Dictionary<string, DiaryEventPromptDef> Fallbacks =
            new Dictionary<string, DiaryEventPromptDef>(StringComparer.OrdinalIgnoreCase);

        public static DiaryEventPromptDef ForKey(string eventType)
        {
            string key = string.IsNullOrWhiteSpace(eventType) ? "Interaction" : eventType;
            List<DiaryEventPromptDef> defs = DefDatabase<DiaryEventPromptDef>.AllDefsListForReading;
            if (defs != null)
            {
                for (int i = 0; i < defs.Count; i++)
                {
                    DiaryEventPromptDef def = defs[i];
                    if (def == null)
                    {
                        continue;
                    }

                    if (string.Equals(def.eventType, key, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(def.defName, key, StringComparison.OrdinalIgnoreCase))
                    {
                        return def;
                    }
                }
            }

            return FallbackFor(key);
        }

        private static DiaryEventPromptDef FallbackFor(string eventType)
        {
            string key = string.IsNullOrWhiteSpace(eventType) ? "Interaction" : eventType;
            DiaryEventPromptDef fallback;
            if (Fallbacks.TryGetValue(key, out fallback))
            {
                return fallback;
            }

            fallback = new DiaryEventPromptDef
            {
                defName = "DiaryEventPrompt_Fallback_" + key,
                eventType = key
            };
            Fallbacks[key] = fallback;
            return fallback;
        }
    }

    /// <summary>
    /// Central lookup helper for prompt templates. Code asks for a stable template key; XML owns the
    /// field list and may override the system prompt or final instruction for that key.
    /// </summary>
    public static class DiaryPromptTemplates
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
        public const string DeathDescription = "DeathDescription";
        public const string ArrivalDescription = "ArrivalDescription";
        public const string Title = "Title";

        private static readonly Dictionary<string, DiaryPromptTemplateDef> Fallbacks =
            new Dictionary<string, DiaryPromptTemplateDef>(StringComparer.OrdinalIgnoreCase);

        public static DiaryPromptTemplateDef ForKey(string templateKey)
        {
            if (!string.IsNullOrWhiteSpace(templateKey))
            {
                List<DiaryPromptTemplateDef> defs = DefDatabase<DiaryPromptTemplateDef>.AllDefsListForReading;
                if (defs != null)
                {
                    for (int i = 0; i < defs.Count; i++)
                    {
                        DiaryPromptTemplateDef def = defs[i];
                        if (def == null)
                        {
                            continue;
                        }

                        if (string.Equals(def.templateKey, templateKey, StringComparison.OrdinalIgnoreCase)
                            || string.Equals(def.defName, templateKey, StringComparison.OrdinalIgnoreCase))
                        {
                            return def;
                        }
                    }
                }
            }

            return FallbackFor(templateKey);
        }

        public static int LoadedTemplateCount
        {
            get
            {
                List<DiaryPromptTemplateDef> defs = DefDatabase<DiaryPromptTemplateDef>.AllDefsListForReading;
                return defs == null ? 0 : defs.Count;
            }
        }

        /// <summary>
        /// Returns the field list to render for a template key: the XML template's fields when it
        /// has any, otherwise the code-defined fallback fields for that shape. This keeps a template
        /// shipped (or patched) with an empty &lt;fields&gt; list from rendering an empty prompt body.
        /// </summary>
        public static List<DiaryPromptFieldDef> FieldsFor(string templateKey)
        {
            DiaryPromptTemplateDef template = ForKey(templateKey);
            if (template?.fields != null && template.fields.Count > 0)
            {
                return template.fields;
            }

            return FallbackFieldsFor(templateKey);
        }

        public static string SystemPromptFor(string templateKey)
        {
            DiaryPromptTemplateDef template = ForKey(templateKey);
            if (!string.IsNullOrWhiteSpace(template?.systemPrompt))
            {
                return template.systemPrompt;
            }

            if (string.Equals(templateKey, DeathDescription, StringComparison.OrdinalIgnoreCase)
                || string.Equals(templateKey, ArrivalDescription, StringComparison.OrdinalIgnoreCase))
            {
                return PawnDiaryMod.Settings == null
                    ? DiaryPrompts.Current.systemPromptNeutral
                    : PawnDiaryMod.Settings.EffectiveNeutralSystemPrompt();
            }

            if (string.Equals(templateKey, SoloDayReflection, StringComparison.OrdinalIgnoreCase))
            {
                return PawnDiaryMod.Settings == null
                    ? DiaryPrompts.Current.systemPromptReflection
                    : PawnDiaryMod.Settings.EffectiveReflectionSystemPrompt();
            }

            if (string.Equals(templateKey, Title, StringComparison.OrdinalIgnoreCase))
            {
                return PawnDiaryMod.Settings == null
                    ? DiaryPrompts.Current.titleSystemPrompt
                    : PawnDiaryMod.Settings.EffectiveTitleSystemPrompt();
            }

            return PawnDiaryMod.Settings == null
                ? DiaryPrompts.Current.systemPrompt
                : PawnDiaryMod.Settings.EffectiveSystemPrompt();
        }

        public static string FinalInstructionFor(string templateKey)
        {
            DiaryPromptTemplateDef template = ForKey(templateKey);
            if (!string.IsNullOrWhiteSpace(template?.finalInstruction))
            {
                return template.finalInstruction;
            }

            if (string.Equals(templateKey, Title, StringComparison.OrdinalIgnoreCase))
            {
                return DiaryPrompts.Current.titleUserInstruction;
            }

            if (string.Equals(templateKey, DeathDescription, StringComparison.OrdinalIgnoreCase))
            {
                return DiaryPrompts.Current.deathDescriptionInstruction;
            }

            if (string.Equals(templateKey, ArrivalDescription, StringComparison.OrdinalIgnoreCase))
            {
                return DiaryPrompts.Current.arrivalDescriptionInstruction;
            }

            return DiaryPrompts.Current.singlePovInstruction;
        }

        public static string RecipientFinalInstruction(string templateKey)
        {
            DiaryPromptTemplateDef template = ForKey(templateKey);
            if (!string.IsNullOrWhiteSpace(template?.recipientFinalInstruction))
            {
                return template.recipientFinalInstruction;
            }

            return DiaryPrompts.Current.recipientFollowupInstruction;
        }

        private static DiaryPromptTemplateDef FallbackFor(string templateKey)
        {
            string key = string.IsNullOrWhiteSpace(templateKey) ? SoloDefault : templateKey;
            DiaryPromptTemplateDef fallback;
            if (Fallbacks.TryGetValue(key, out fallback))
            {
                return fallback;
            }

            fallback = new DiaryPromptTemplateDef
            {
                defName = "DiaryPromptTemplate_Fallback_" + key,
                templateKey = key,
                fields = FallbackFieldsFor(key)
            };
            Fallbacks[key] = fallback;
            return fallback;
        }

        private static List<DiaryPromptFieldDef> FallbackFieldsFor(string templateKey)
        {
            if (string.Equals(templateKey, DeathDescription, StringComparison.OrdinalIgnoreCase))
            {
                return Fields(
                    Field("event", "EventNoun"),
                    Field("event prompt", "EventPrompt"),
                    Field("event enhancement", "EventEnhancement"),
                    Field("deceased", "DeathVictim"),
                    Field("what happened", "NeutralText"),
                    Field("death facts", "DeathFacts"),
                    Field("deceased pawn", "DeathPawnSummary"),
                    Field("setting", "Setting"));
            }

            if (string.Equals(templateKey, ArrivalDescription, StringComparison.OrdinalIgnoreCase))
            {
                return Fields(
                    Field("event", "EventNoun"),
                    Field("event prompt", "EventPrompt"),
                    Field("event enhancement", "EventEnhancement"),
                    Field("colonist", "ArrivalPawn"),
                    Field("what happened", "NeutralText"),
                    Field("arrival facts", "ArrivalFacts"),
                    Field("colonist pawn", "PawnSummary"),
                    Field("setting", "Setting"));
            }

            if (string.Equals(templateKey, Title, StringComparison.OrdinalIgnoreCase))
            {
                return Fields(Field("entry", "EntryText"));
            }

            // Persona is intentionally absent: it is injected into the system prompt
            // (DiaryPromptPlanner.Build via PromptAssembler.ComposeSystem), not rendered as a user-message field.
            return Fields(
                Field("event", "EventNoun"),
                Field("pov", "PovName"),
                Field("what happened", "PovText"),
                Field("event prompt", "EventPrompt"),
                Field("event enhancement", "EventEnhancement"),
                ContextField("ritual role", "ritual_role"),
                ContextField("ritual title", "ritual_title"),
                ContextField("royal title", "royal_title"),
                ContextField("ideoligion role", "ideological_role"),
                Field("instruction", "Instruction"),
                Field("important context", "PromptEnchantment"),
                Field("setting", "Setting"),
                Field("my last opener (not repeat)", "LastOpener"));
        }

        private static List<DiaryPromptFieldDef> Fields(params DiaryPromptFieldDef[] fields)
        {
            return fields.ToList();
        }

        private static DiaryPromptFieldDef Field(string label, string source)
        {
            return new DiaryPromptFieldDef { label = label, source = source };
        }

        private static DiaryPromptFieldDef ContextField(string label, string contextKey)
        {
            return new DiaryPromptFieldDef { label = label, source = "GameContext", contextKey = contextKey };
        }
    }

    /// <summary>
    /// XML policy for context that C# tracks from live game state but prompt tuning should control,
    /// such as active map conditions or the freshest threat letter.
    /// </summary>
    public class DiaryContextReactionDef : Def
    {
        public string reactionKey;
        public bool enabled = true;
        public string textKey;
        public int maxItems = -1;
        public int scanBack = -1;
        public int timeoutTicks = -1;
        public bool requireHomeMap = true;
        public bool requireDanger = true;
        public bool displayOnUiOnly = true;
        public List<string> letterDefNames;
    }

    /// <summary>
    /// Lookup helper for context reaction policies. New C# trackers can add a reactionKey and XML can
    /// decide whether/how it appears without creating more settings fields.
    /// </summary>
    public static class DiaryContextReactions
    {
        public const string ActiveMapConditions = "ActiveMapConditions";
        public const string RecentThreatLetter = "RecentThreatLetter";

        public static DiaryContextReactionDef ForKey(string reactionKey)
        {
            List<DiaryContextReactionDef> defs = DefDatabase<DiaryContextReactionDef>.AllDefsListForReading;
            if (defs != null)
            {
                for (int i = 0; i < defs.Count; i++)
                {
                    DiaryContextReactionDef def = defs[i];
                    if (def == null)
                    {
                        continue;
                    }

                    if (string.Equals(def.reactionKey, reactionKey, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(def.defName, reactionKey, StringComparison.OrdinalIgnoreCase))
                    {
                        return def;
                    }
                }
            }

            return Fallback(reactionKey);
        }

        public static bool Enabled(string reactionKey)
        {
            return ForKey(reactionKey).enabled;
        }

        public static string Format(string reactionKey, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            DiaryContextReactionDef policy = ForKey(reactionKey);
            string key = policy.textKey;
            if (string.IsNullOrWhiteSpace(key))
            {
                key = string.Equals(reactionKey, RecentThreatLetter, StringComparison.OrdinalIgnoreCase)
                    ? "PawnDiary.Ctx.RecentThreat"
                    : "PawnDiary.Ctx.ActiveConditions";
            }

            return key.Translate(value).Resolve();
        }

        public static int MaxItems(string reactionKey, int fallback)
        {
            int value = ForKey(reactionKey).maxItems;
            return value >= 0 ? value : fallback;
        }

        public static int ScanBack(string reactionKey, int fallback)
        {
            int value = ForKey(reactionKey).scanBack;
            return value >= 0 ? value : fallback;
        }

        public static int TimeoutTicks(string reactionKey, int fallback)
        {
            int value = ForKey(reactionKey).timeoutTicks;
            return value >= 0 ? value : fallback;
        }

        public static bool LetterDefAllowed(DiaryContextReactionDef policy, string defName)
        {
            if (policy == null || string.IsNullOrWhiteSpace(defName))
            {
                return false;
            }

            List<string> allowed = policy.letterDefNames;
            if (allowed == null || allowed.Count == 0)
            {
                allowed = new List<string> { "ThreatBig", "ThreatSmall" };
            }

            for (int i = 0; i < allowed.Count; i++)
            {
                if (string.Equals(defName, allowed[i], StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        // Fallback policies are cached per key so a missing/renamed Def does not allocate a new
        // object on every Enabled/Format/MaxItems call. The real Def always wins because ForKey
        // scans the DefDatabase first and only reaches the cache on a miss.
        private static readonly Dictionary<string, DiaryContextReactionDef> FallbackCache =
            new Dictionary<string, DiaryContextReactionDef>(StringComparer.OrdinalIgnoreCase);

        private static DiaryContextReactionDef Fallback(string reactionKey)
        {
            string key = reactionKey ?? string.Empty;
            DiaryContextReactionDef cached;
            if (FallbackCache.TryGetValue(key, out cached))
            {
                return cached;
            }

            DiaryContextReactionDef fallback = string.Equals(reactionKey, RecentThreatLetter, StringComparison.OrdinalIgnoreCase)
                ? new DiaryContextReactionDef
                {
                    defName = "DiaryContextReaction_Fallback_RecentThreatLetter",
                    reactionKey = RecentThreatLetter,
                    textKey = "PawnDiary.Ctx.RecentThreat",
                    scanBack = 30,
                    timeoutTicks = 7500,
                    requireHomeMap = true,
                    requireDanger = true,
                    letterDefNames = new List<string> { "ThreatBig", "ThreatSmall" }
                }
                : new DiaryContextReactionDef
                {
                    defName = "DiaryContextReaction_Fallback_ActiveMapConditions",
                    reactionKey = ActiveMapConditions,
                    textKey = "PawnDiary.Ctx.ActiveConditions",
                    maxItems = 3,
                    displayOnUiOnly = true
                };

            FallbackCache[key] = fallback;
            return fallback;
        }
    }
}
