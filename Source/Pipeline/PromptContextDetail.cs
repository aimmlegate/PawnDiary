// Prompt-context detail contracts and selector.
//
// The live game always captures the full context snapshot. These helpers decide how much of that
// already-captured context is rendered into the LLM prompt for a specific request. Keep this pure:
// no RimWorld, no DefDatabase, no settings reads, no RNG, no IO.
using System;
using System.Collections.Generic;

namespace PawnDiary
{
    /// <summary>
    /// User-facing prompt context presets. Full preserves the old prompt shape; Balanced and Compact
    /// keep required facts and then dynamically choose the most relevant optional fields.
    /// </summary>
    public enum PromptContextDetailLevel
    {
        Full,
        Balanced,
        Compact
    }

    /// <summary>
    /// Per-API-lane context override. Inherit means use the global setting.
    /// </summary>
    public enum PromptContextDetailOverride
    {
        Inherit,
        Full,
        Balanced,
        Compact
    }

    /// <summary>One field's selector diagnostics for prompt previews and debug UI.</summary>
    internal class PromptContextFieldReport
    {
        public string label = string.Empty;
        public string source = string.Empty;
        public string contextKey = string.Empty;
        public string valuePreview = string.Empty;
        public int score;
        public int chars;
        public bool required;
        public string reason = string.Empty;
    }

    /// <summary>Selector diagnostics: what was kept, what was cut, and why.</summary>
    internal class PromptContextSelectionReport
    {
        public PromptContextDetailLevel level = PromptContextDetailLevel.Full;
        public int budgetChars;
        public int inputChars;
        public int outputChars;
        public List<PromptContextFieldReport> kept = new List<PromptContextFieldReport>();
        public List<PromptContextFieldReport> cut = new List<PromptContextFieldReport>();
    }

    /// <summary>Selected prompt fields plus the report that explains that selection.</summary>
    internal class PromptContextSelectionResult
    {
        public List<DiaryPromptFieldPolicy> fields = new List<DiaryPromptFieldPolicy>();
        public PromptContextSelectionReport report = new PromptContextSelectionReport();
    }

    /// <summary>
    /// Deterministic budgeted selector for prompt fields. It never summarizes or rewrites values; it
    /// only keeps or drops complete fields so the saved prompt is easy to audit.
    /// </summary>
    internal static class PromptContextSelector
    {
        private const int FullBudget = int.MaxValue;
        private const int BalancedDefaultBudget = 1400;
        private const int CompactDefaultBudget = 750;
        private const int BalancedReflectionBudget = 1900;
        private const int CompactReflectionBudget = 1150;
        private const int BalancedNeutralBudget = 1250;
        private const int CompactNeutralBudget = 850;

        private sealed class Candidate
        {
            public DiaryPromptFieldPolicy field;
            public int index;
            public int score;
            public int chars;
            public bool required;
            public string reason;
            public string value;
        }

        /// <summary>
        /// Returns the fields that should be rendered for one context level. Full is pass-through;
        /// Balanced/Compact always keep core facts and then fill the remaining budget by score.
        /// </summary>
        public static PromptContextSelectionResult Select(
            string templateKey,
            List<DiaryPromptFieldPolicy> fields,
            PromptValues values,
            string domain,
            string gameContext,
            PromptContextDetailLevel level)
        {
            PromptContextDetailLevel normalizedLevel = Normalize(level);
            List<Candidate> candidates = BuildCandidates(templateKey, fields, values, domain, gameContext, normalizedLevel);
            PromptContextSelectionResult result = new PromptContextSelectionResult();
            result.report.level = normalizedLevel;
            result.report.budgetChars = BudgetFor(templateKey, normalizedLevel);
            result.report.inputChars = TotalChars(candidates);

            if (normalizedLevel == PromptContextDetailLevel.Full)
            {
                for (int i = 0; i < candidates.Count; i++)
                {
                    Candidate candidate = candidates[i];
                    result.fields.Add(Copy(candidate.field));
                    result.report.kept.Add(Report(candidate, "kept by Full context"));
                }

                result.report.outputChars = result.report.inputChars;
                return result;
            }

            List<Candidate> required = new List<Candidate>();
            List<Candidate> optional = new List<Candidate>();
            for (int i = 0; i < candidates.Count; i++)
            {
                Candidate candidate = candidates[i];
                if (candidate.required)
                {
                    required.Add(candidate);
                }
                else
                {
                    optional.Add(candidate);
                }
            }

            int used = 0;
            List<Candidate> selected = new List<Candidate>();
            for (int i = 0; i < required.Count; i++)
            {
                selected.Add(required[i]);
                used += required[i].chars;
            }

            optional.Sort(CompareCandidates);
            int budget = result.report.budgetChars;
            for (int i = 0; i < optional.Count; i++)
            {
                Candidate candidate = optional[i];
                if (used + candidate.chars <= budget)
                {
                    selected.Add(candidate);
                    used += candidate.chars;
                }
                else
                {
                    result.report.cut.Add(Report(candidate, CutReason(candidate, normalizedLevel)));
                }
            }

            selected.Sort(CompareCandidateOrder);
            for (int i = 0; i < selected.Count; i++)
            {
                Candidate candidate = selected[i];
                result.fields.Add(Copy(candidate.field));
                result.report.kept.Add(Report(candidate, candidate.required ? "required core context" : candidate.reason));
            }

            result.report.outputChars = TotalReportChars(result.report.kept);
            return result;
        }

        /// <summary>Normalizes invalid enum values from hand-edited settings or old saves.</summary>
        public static PromptContextDetailLevel Normalize(PromptContextDetailLevel level)
        {
            switch (level)
            {
                case PromptContextDetailLevel.Balanced:
                case PromptContextDetailLevel.Compact:
                    return level;
                default:
                    return PromptContextDetailLevel.Full;
            }
        }

        /// <summary>Normalizes per-lane override values from hand-edited settings or old saves.</summary>
        public static PromptContextDetailOverride NormalizeOverride(PromptContextDetailOverride value)
        {
            switch (value)
            {
                case PromptContextDetailOverride.Full:
                case PromptContextDetailOverride.Balanced:
                case PromptContextDetailOverride.Compact:
                    return value;
                default:
                    return PromptContextDetailOverride.Inherit;
            }
        }

        /// <summary>Resolves one lane's override against the global context detail setting.</summary>
        public static PromptContextDetailLevel Resolve(PromptContextDetailLevel globalLevel, PromptContextDetailOverride laneOverride)
        {
            switch (NormalizeOverride(laneOverride))
            {
                case PromptContextDetailOverride.Full:
                    return PromptContextDetailLevel.Full;
                case PromptContextDetailOverride.Balanced:
                    return PromptContextDetailLevel.Balanced;
                case PromptContextDetailOverride.Compact:
                    return PromptContextDetailLevel.Compact;
                default:
                    return Normalize(globalLevel);
            }
        }

        private static List<Candidate> BuildCandidates(
            string templateKey,
            List<DiaryPromptFieldPolicy> fields,
            PromptValues values,
            string domain,
            string gameContext,
            PromptContextDetailLevel level)
        {
            List<Candidate> result = new List<Candidate>();
            if (fields == null)
            {
                return result;
            }

            for (int i = 0; i < fields.Count; i++)
            {
                DiaryPromptFieldPolicy field = fields[i];
                if (field == null || !field.enabled)
                {
                    continue;
                }

                string value = PromptAssembler.ResolveFieldValue(field.source, field.contextKey, values);
                if (!HasRenderableValue(value))
                {
                    continue;
                }

                string label = string.IsNullOrWhiteSpace(field.label) ? field.source : field.label;
                bool required = IsRequired(templateKey, field);
                string reason = string.Empty;
                int score = required ? 1000 : Score(field, value, domain, gameContext, templateKey, level, out reason);
                result.Add(new Candidate
                {
                    field = field,
                    index = i,
                    value = value.Trim(),
                    required = required,
                    score = score,
                    chars = RenderedChars(label, value),
                    reason = required ? "required core context" : reason
                });
            }

            return result;
        }

        private static bool IsRequired(string templateKey, DiaryPromptFieldPolicy field)
        {
            string source = field?.source ?? string.Empty;
            string contextKey = field?.contextKey ?? string.Empty;
            if (Eq(source, "EventNoun")
                || Eq(source, "PovName")
                || Eq(source, "PovRole")
                || Eq(source, "OtherPawnName")
                || Eq(source, "PovText")
                || Eq(source, "WhatHappened")
                || Eq(source, "WhatYouSaw")
                || Eq(source, "NeutralText")
                || Eq(source, "Instruction")
                || Eq(source, "EventPrompt")
                || Eq(source, "DeathVictim")
                || Eq(source, "DeathFacts")
                || Eq(source, "ArrivalPawn")
                || Eq(source, "ArrivalFacts")
                || Eq(source, "EntryText"))
            {
                return true;
            }

            if (Eq(source, "DeathPawnSummary"))
            {
                return true;
            }

            if (Eq(source, "PawnSummary") && Eq(templateKey, DiaryPipelineTemplates.ArrivalDescription))
            {
                return true;
            }

            if (Eq(source, "GameContext")
                && (Eq(contextKey, ExternalEventRequestText.PromptInstructionContextKey)
                    || Eq(contextKey, ExternalEventRequestText.PromptFragmentContextKey)
                    || Eq(contextKey, "quadrum")
                    || Eq(contextKey, "quadrum_dates")
                    || Eq(contextKey, "arc_year")))
            {
                return true;
            }

            return false;
        }

        private static int Score(
            DiaryPromptFieldPolicy field,
            string value,
            string domain,
            string gameContext,
            string templateKey,
            PromptContextDetailLevel level,
            out string reason)
        {
            string source = field?.source ?? string.Empty;
            string contextKey = field?.contextKey ?? string.Empty;
            string loweredValue = (value ?? string.Empty).ToLowerInvariant();
            bool combat = DomainOrContext(domain, gameContext, "Raid") || DomainOrContext(domain, gameContext, "MentalState") || HasAnyMarker(gameContext, "raid=", "mental_state=");
            bool quest = DomainOrContext(domain, gameContext, "Quest") || HasAnyMarker(gameContext, "quest=", "quest_signal=");
            bool progression = DomainOrContext(domain, gameContext, "Progression") || HasAnyMarker(gameContext, "progression=", "skill=", "psylink_level=", "xenotype=", "title=");
            bool ability = DomainOrContext(domain, gameContext, "Ability") || HasAnyMarker(gameContext, "ability=");
            bool ritual = DomainOrContext(domain, gameContext, "Ritual") || HasAnyMarker(gameContext, "ritual=");
            bool social = HasAnyMarker(gameContext, "worker=Interaction_", "romance=", "kind=married", "def=Insult");

            if (Eq(source, "EventEnhancement"))
            {
                reason = "event guidance";
                return level == PromptContextDetailLevel.Compact ? 55 : 78;
            }

            if (Eq(source, "PawnSummary"))
            {
                bool highSignal = ContainsAny(loweredValue, "health=", "low_capacities=", "thoughts=", "mood=", "pain", "bleed", "injur", "sick");
                reason = highSignal ? "pawn state contains health, mood, or thought context" : "pawn identity context";
                return highSignal ? 82 : 62;
            }

            if (Eq(source, "PromptEnchantment"))
            {
                bool severe = ContainsAny(loweredValue, "severe", "major", "critical", "bleed", "pain", "infection", "threat", "danger", "death", "conscious");
                reason = severe ? "severe live status context" : "optional live status context";
                return severe ? 88 : 66;
            }

            if (Eq(source, "Setting") || Eq(source, "DeathSetting"))
            {
                bool dramatic = ContainsAny(loweredValue, "raid", "threat", "danger", "fire", "toxic", "fallout", "storm", "cold", "hot", "blood", "corpse");
                reason = dramatic ? "setting includes threat, weather, or danger" : "ordinary setting context";
                return dramatic ? 78 : 48;
            }

            if (Eq(source, "Tone"))
            {
                reason = "tone guidance";
                return level == PromptContextDetailLevel.Compact ? 24 : 44;
            }

            if (Eq(source, "Relationship"))
            {
                reason = social ? "social relationship context" : "relationship context";
                return social ? 76 : 55;
            }

            if (Eq(source, "Weapon"))
            {
                reason = combat ? "combat tool context" : "equipment context";
                return combat ? 88 : 38;
            }

            if (Eq(source, "HiddenInitiatorEntry"))
            {
                reason = "paired-entry continuity";
                return level == PromptContextDetailLevel.Compact ? 35 : 64;
            }

            if (Eq(source, "LastOpener"))
            {
                reason = "anti-repetition hint";
                return level == PromptContextDetailLevel.Compact ? 5 : 22;
            }

            if (Eq(source, "PreviousEntryEnding"))
            {
                reason = "diary continuity hint";
                return level == PromptContextDetailLevel.Compact ? 14 : 36;
            }

            if (Eq(source, "GameContext"))
            {
                return ScoreContextKey(contextKey, quest, progression, ability, ritual, combat, level, out reason);
            }

            reason = "optional context";
            return 40;
        }

        private static int ScoreContextKey(string contextKey, bool quest, bool progression, bool ability, bool ritual, bool combat, PromptContextDetailLevel level, out string reason)
        {
            if (StartsWithAny(contextKey, "quest_"))
            {
                reason = quest ? "quest-specific context" : "quest context";
                return quest ? 82 : 42;
            }

            if (StartsWithAny(contextKey, "ritual_") || Eq(contextKey, "ideological_role") || Eq(contextKey, "royal_title"))
            {
                reason = ritual ? "ritual-specific context" : "role/title context";
                return ritual ? 80 : 50;
            }

            if (StartsWithAny(contextKey, "ability_"))
            {
                if (Eq(contextKey, "ability_cooldown_ticks"))
                {
                    reason = "numeric ability metadata";
                    return level == PromptContextDetailLevel.Compact ? 8 : 28;
                }

                reason = ability ? "ability-specific context" : "ability context";
                return ability ? 82 : 42;
            }

            if (Eq(contextKey, "arrival_mode") || Eq(contextKey, "strategy"))
            {
                reason = combat ? "raid tactic context" : "incident tactic context";
                return combat ? 84 : 40;
            }

            if (Eq(contextKey, "points"))
            {
                reason = "numeric raid strength metadata";
                return level == PromptContextDetailLevel.Compact ? 8 : 24;
            }

            if (ContainsAny(contextKey, "progression", "skill", "psylink", "xenotype", "title", "passion"))
            {
                reason = progression ? "progression-specific context" : "progression context";
                return progression ? 84 : 45;
            }

            if (ContainsAny(contextKey, "selected_memories", "candidate_memories", "important_entries", "entries_this_year", "forced"))
            {
                reason = Eq(contextKey, "forced") ? "arc bookkeeping metadata" : "reflection bookkeeping context";
                return Eq(contextKey, "forced") ? 20 : 42;
            }

            reason = "optional game-context field";
            return 36;
        }

        private static int CompareCandidates(Candidate left, Candidate right)
        {
            int score = right.score.CompareTo(left.score);
            return score != 0 ? score : left.index.CompareTo(right.index);
        }

        private static int CompareCandidateOrder(Candidate left, Candidate right)
        {
            return left.index.CompareTo(right.index);
        }

        private static string CutReason(Candidate candidate, PromptContextDetailLevel level)
        {
            return candidate.reason + "; cut by " + level + " budget before higher-priority context";
        }

        private static PromptContextFieldReport Report(Candidate candidate, string reason)
        {
            return new PromptContextFieldReport
            {
                label = candidate.field.label ?? string.Empty,
                source = candidate.field.source ?? string.Empty,
                contextKey = candidate.field.contextKey ?? string.Empty,
                valuePreview = Preview(candidate.value),
                score = candidate.score,
                chars = candidate.chars,
                required = candidate.required,
                reason = reason ?? string.Empty
            };
        }

        private static DiaryPromptFieldPolicy Copy(DiaryPromptFieldPolicy field)
        {
            return new DiaryPromptFieldPolicy
            {
                enabled = field.enabled,
                label = field.label,
                source = field.source,
                contextKey = field.contextKey
            };
        }

        private static int BudgetFor(string templateKey, PromptContextDetailLevel level)
        {
            if (level == PromptContextDetailLevel.Full)
            {
                return FullBudget;
            }

            bool reflection = Eq(templateKey, DiaryPipelineTemplates.SoloDayReflection)
                || Eq(templateKey, DiaryPipelineTemplates.SoloQuadrumReflection)
                || Eq(templateKey, DiaryPipelineTemplates.SoloArcReflection);
            bool neutral = Eq(templateKey, DiaryPipelineTemplates.DeathDescription)
                || Eq(templateKey, DiaryPipelineTemplates.ArrivalDescription);
            if (level == PromptContextDetailLevel.Balanced)
            {
                return reflection ? BalancedReflectionBudget : neutral ? BalancedNeutralBudget : BalancedDefaultBudget;
            }

            return reflection ? CompactReflectionBudget : neutral ? CompactNeutralBudget : CompactDefaultBudget;
        }

        private static int TotalChars(List<Candidate> candidates)
        {
            int total = 0;
            for (int i = 0; i < candidates.Count; i++)
            {
                total += candidates[i].chars;
            }

            return total;
        }

        private static int TotalReportChars(List<PromptContextFieldReport> reports)
        {
            int total = 0;
            for (int i = 0; i < reports.Count; i++)
            {
                total += reports[i].chars;
            }

            return total;
        }

        private static int RenderedChars(string label, string value)
        {
            return (label == null ? 0 : label.Length) + 2 + (value == null ? 0 : value.Trim().Length) + 1;
        }

        private static bool HasRenderableValue(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            string trimmed = value.Trim();
            return !Eq(trimmed, "none") && !Eq(trimmed, "n/a") && !Eq(trimmed, "unknown");
        }

        private static string Preview(string value)
        {
            string text = (value ?? string.Empty).Trim();
            return text.Length <= 120 ? text : text.Substring(0, 117) + "...";
        }

        private static bool DomainOrContext(string domain, string gameContext, string marker)
        {
            return Contains(domain, marker) || Contains(gameContext, marker);
        }

        private static bool HasAnyMarker(string text, params string[] markers)
        {
            for (int i = 0; i < markers.Length; i++)
            {
                if (Contains(text, markers[i]))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ContainsAny(string text, params string[] markers)
        {
            return HasAnyMarker(text, markers);
        }

        private static bool StartsWithAny(string text, params string[] prefixes)
        {
            string value = text ?? string.Empty;
            for (int i = 0; i < prefixes.Length; i++)
            {
                if (value.StartsWith(prefixes[i], StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool Contains(string text, string marker)
        {
            return !string.IsNullOrEmpty(text)
                && !string.IsNullOrEmpty(marker)
                && text.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool Eq(string left, string right)
        {
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }
    }
}
