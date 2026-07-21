// Pure, game-independent prompt assembly. Turns an already-resolved bag of string values plus a
// template's field list into the final USER prompt, and composes the SYSTEM prompt from a base
// prompt and an optional writing-style block.
//
// "Pure" means: no RimWorld / Verse / Unity types, no DefDatabase, no .Translate(), no RNG, no IO.
// The same inputs always produce the same strings. The only thing it leans on is DiaryContextFields
// (also pure) for the GameContext source.
//
// Why it exists: the in-game extractor (DiaryContextBuilder + persona/enchantment selection) reads
// live game state and is C#-only and impure. But the *rendering algorithm* — field order, the skip
// rules for empty/placeholder values, the "label: value" join, the instruction trailer, and the
// style/system composition — is also re-implemented by the Node prompt-lab harness. Keeping that
// algorithm in one pure place lets a tiny dotnet dump tool render a set of fixed inputs and let the
// harness assert byte-identical output, so the two implementations can never silently drift
// (see prompt-lab/golden and prompt-lab/dump).
//
// New to C#? Read this as a pure function module: DiaryPromptBuilder does the impure work of reading
// the game and filling these plain data objects, then calls Render / RenderUserPrompt / ComposeSystem.
using System;
using System.Collections.Generic;

namespace PawnDiary
{
    /// <summary>
    /// One field in a prompt template: a model-facing <see cref="label"/> and the stable
    /// <see cref="source"/> token that selects which value fills it. <see cref="contextKey"/> is only
    /// used by the <c>GameContext</c> source.
    /// </summary>
    internal class PromptAssemblerField
    {
        public string label;
        public string source;
        public string contextKey;
        public bool enabled = true;
    }

    /// <summary>
    /// The "context bag": every value the renderer can place into a field, already resolved to a plain
    /// string by the caller. This is the boundary between the impure game-state extractor and the pure
    /// renderer. (Named <c>eventNoun</c>/<c>povName</c> rather than <c>event</c>/<c>pov</c> because
    /// <c>event</c> is a C# keyword; the JSON contract in prompt-lab uses these same names.)
    /// </summary>
    internal class PromptValues
    {
        public string eventNoun;
        public string povName;
        public string povRole;
        public string otherName;
        public string povText;
        public string neutralText;
        public string instruction;
        public string pawnSummary;
        public string persona;
        public string eventPrompt;
        public string eventEnhancement;
        public string promptEnchantment;
        public bool includePromptEnchantment = true;
        public string setting;
        public string tone;
        public string relationship;
        public string narrativeContext;
        public string memoryContext;
        public string beliefContext;
        public string lastOpener;
        public string previousEntryEnding;
        public string weapon;
        public string initiatorEntry;
        public string deathVictim;
        public string deathFacts;
        public string deathPawnSummary;
        public string deathSetting;
        public string arrivalPawn;
        public string arrivalFacts;
        public string entryText;
        public string gameContext;
    }

    /// <summary>Everything the pure renderer needs to produce one request's prompts.</summary>
    internal class PromptAssemblerInput
    {
        public string templateKey;
        public List<PromptAssemblerField> fields = new List<PromptAssemblerField>();
        public bool includePersona = true;
        public PromptValues values = new PromptValues();
        public string baseSystemPrompt;
        public string personaVoiceBlock;
        public string finalInstruction;
    }

    /// <summary>The assembled system and user prompts.</summary>
    internal class PromptAssemblerResult
    {
        public string systemPrompt;
        public string userPrompt;
    }

    /// <summary>Pure prompt assembly shared by the live mod and the prompt-lab test harness.</summary>
    internal static class PromptAssembler
    {
        /// <summary>Assembles both prompts from a single input. Used by the dump tool and tests; the
        /// live mod calls <see cref="RenderUserPrompt"/> and <see cref="ComposeSystem"/> separately.</summary>
        public static PromptAssemblerResult Render(PromptAssemblerInput input)
        {
            if (input == null)
            {
                return new PromptAssemblerResult { systemPrompt = string.Empty, userPrompt = string.Empty };
            }

            return new PromptAssemblerResult
            {
                systemPrompt = ComposeSystem(input.baseSystemPrompt, input.personaVoiceBlock, input.includePersona),
                userPrompt = RenderUserPrompt(input.fields, input.values, input.finalInstruction)
            };
        }

        /// <summary>
        /// Renders the structured "label: value" body for the given fields — dropping empty and
        /// placeholder values — then appends the final instruction as a trailing paragraph.
        /// </summary>
        public static string RenderUserPrompt(List<PromptAssemblerField> fields, PromptValues values, string finalInstruction)
        {
            List<string> lines = new List<string>();
            if (fields != null)
            {
                for (int i = 0; i < fields.Count; i++)
                {
                    PromptAssemblerField field = fields[i];
                    if (field == null || !field.enabled)
                    {
                        continue;
                    }

                    string label = string.IsNullOrWhiteSpace(field.label) ? field.source : field.label;
                    AppendField(lines, label, ResolveSource(field, values));
                }
            }

            string body = string.Join("\n", lines.ToArray());
            if (string.IsNullOrWhiteSpace(finalInstruction))
            {
                return body;
            }

            if (string.IsNullOrWhiteSpace(body))
            {
                return finalInstruction;
            }

            return body + "\n\n" + finalInstruction;
        }

        /// <summary>
        /// Composes the system prompt: the base prompt plus the writing-style block appended as a
        /// trailing paragraph, unless the template opts out of style or no block was supplied.
        /// </summary>
        public static string ComposeSystem(string baseSystemPrompt, string personaVoiceBlock, bool includePersona)
        {
            string baseText = baseSystemPrompt ?? string.Empty;
            if (!includePersona || string.IsNullOrWhiteSpace(personaVoiceBlock))
            {
                return baseText;
            }

            if (string.IsNullOrWhiteSpace(baseText))
            {
                return personaVoiceBlock;
            }

            return baseText.TrimEnd() + "\n\n" + personaVoiceBlock;
        }

        /// <summary>
        /// Resolves one source/contextKey pair exactly as the renderer will. The context selector uses
        /// this public pure helper so preview reports and the final prompt cannot drift.
        /// </summary>
        public static string ResolveFieldValue(string source, string contextKey, PromptValues values)
        {
            return ResolveSource(new PromptAssemblerField { source = source, contextKey = contextKey }, values);
        }

        // Maps a stable source token to its value in the bag. Mirrors the Node harness's sourceValue;
        // both must agree (the golden check enforces it).
        private static string ResolveSource(PromptAssemblerField field, PromptValues v)
        {
            if (field == null || v == null)
            {
                return string.Empty;
            }

            string source = field.source ?? string.Empty;
            if (Eq(source, "EventNoun")) return v.eventNoun;
            if (Eq(source, "PovName")) return v.povName;
            if (Eq(source, "PovRole")) return v.povRole;
            if (Eq(source, "OtherPawnName")) return v.otherName;
            if (Eq(source, "PovText") || Eq(source, "WhatHappened") || Eq(source, "WhatYouSaw")) return v.povText;
            if (Eq(source, "NeutralText")) return v.neutralText;
            if (Eq(source, "Instruction")) return v.instruction;
            if (Eq(source, "PawnSummary")) return v.pawnSummary;
            if (Eq(source, "Persona")) return v.persona;
            if (Eq(source, "EventPrompt")) return v.eventPrompt;
            if (Eq(source, "EventEnhancement")) return v.eventEnhancement;
            if (Eq(source, "PromptEnchantment")) return v.includePromptEnchantment ? v.promptEnchantment : string.Empty;
            if (Eq(source, "Setting")) return v.setting;
            if (Eq(source, "Tone")) return v.tone;
            if (Eq(source, "Relationship")) return v.relationship;
            // Literal schema tokens keep this tiny pure renderer linkable by prompt-lab without
            // pulling in the separate narrative, memory, and belief policy implementations.
            if (Eq(source, "NarrativeContext")) return v.narrativeContext;
            if (Eq(source, "MemoryContext")) return v.memoryContext;
            if (Eq(source, "BeliefContext")) return v.beliefContext;
            if (Eq(source, "LastOpener")) return v.lastOpener;
            if (Eq(source, "PreviousEntryEnding")) return v.previousEntryEnding;
            if (Eq(source, "Weapon")) return v.weapon;
            if (Eq(source, "HiddenInitiatorEntry")) return v.initiatorEntry;
            if (Eq(source, "DeathVictim")) return v.deathVictim;
            if (Eq(source, "DeathFacts")) return v.deathFacts;
            if (Eq(source, "DeathPawnSummary")) return v.deathPawnSummary;
            if (Eq(source, "DeathSetting")) return v.deathSetting;
            if (Eq(source, "ArrivalPawn")) return v.arrivalPawn;
            if (Eq(source, "ArrivalFacts")) return v.arrivalFacts;
            if (Eq(source, "EntryText")) return v.entryText;
            if (Eq(source, "GameContext")) return DiaryContextFields.Value(v.gameContext, field.contextKey);
            return string.Empty;
        }

        // Adds "label: value" only when the value carries real signal. Empty strings and the
        // placeholder values "none"/"n/a"/"unknown" are skipped so they cost no tokens.
        private static void AppendField(List<string> lines, string label, string value)
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

            lines.Add(label + ": " + trimmed);
        }

        private static bool Eq(string a, string b)
        {
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }
    }
}
