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
    /// Character budgets for the Balanced/Compact presets, split by prompt family (standard entry,
    /// reflection, and neutral death/arrival). Pure data with code-default values; the impure
    /// <c>ContextDetailPolicy</c> supplies XML-tuned overrides. Full is unbudgeted and not stored here.
    /// Keeping the numbers here (rather than as C# consts) lets them be retuned from XML per AGENTS.md.
    /// </summary>
    internal sealed class PromptContextBudgets
    {
        // Tuned so Balanced/Compact visibly trim optional context on ordinary events. The old values
        // (1400/750/1900/1150/1250/850) were so generous that Balanced never cut anything and Compact
        // only cut the richest events. These match 1.6/Defs/DiaryContextDetailDef.xml; both are
        // XML-tweakable there, and a non-positive authored value falls back to the numbers here.
        public int balancedDefault = 650;
        public int compactDefault = 350;
        public int balancedReflection = 1000;
        public int compactReflection = 600;
        public int balancedNeutral = 600;
        public int compactNeutral = 400;

        /// <summary>Built-in defaults, used whenever no XML-backed budgets are injected.</summary>
        public static readonly PromptContextBudgets Defaults = new PromptContextBudgets();
    }

    /// <summary>
    /// Deterministic budgeted selector for prompt fields. It never summarizes or rewrites values; it
    /// only keeps or drops complete fields so the saved prompt is easy to audit.
    /// </summary>
    internal static class PromptContextSelector
    {
        private const int FullBudget = int.MaxValue;

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
            PromptContextDetailLevel level,
            PromptContextBudgets budgets = null)
        {
            PromptContextDetailLevel normalizedLevel = Normalize(level);
            List<Candidate> candidates = BuildCandidates(templateKey, fields, values, domain, gameContext, normalizedLevel);
            PromptContextSelectionResult result = new PromptContextSelectionResult();
            result.report.level = normalizedLevel;
            result.report.budgetChars = BudgetFor(templateKey, normalizedLevel, budgets);
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

            // A non-empty frozen memory recall is required in EVERY detail preset
            // (LORE_MEMORY_SEED_PLAN §9): recall metadata was already bumped when the memory was
            // selected, so a late Balanced/Compact budget cut would create a phantom recall. The
            // value stays bounded by the universal two-line/500-char memory policy, not by the
            // preset's soft budget. Empty recall never reaches candidates (no renderable value).
            if (Eq(source, MemoryContextPrompt.Source))
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
                    || Eq(contextKey, "arc_year")
                    || IsRequiredQuestContextKey(contextKey)
                    || IsRequiredBiotechContextKey(contextKey)
                    || IsRequiredRoyaltyContextKey(contextKey)
                    || IsRequiredOdysseyContextKey(contextKey)))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Keeps the exact quest lifecycle edge intelligible in every detail preset. The root Def,
        /// quest-instance ID, and continuity arc are deliberately not prompt fields; only the visible
        /// quest label and proven accepted/completed/failed signal survive Compact budgeting.
        /// </summary>
        private static bool IsRequiredQuestContextKey(string contextKey)
        {
            return Eq(contextKey, "quest_label") || Eq(contextKey, "quest_signal");
        }

        /// <summary>
        /// Keeps the small, event-defining Biotech B1 facts in every prompt-detail preset. These are
        /// stable context-schema tokens (not tunable prose): Compact may discard supporting descriptions,
        /// participant lists, and continuity, but it must not turn a growth choice or birth into an
        /// ambiguous generic event. Internal IDs, raw band tokens, numeric tiers, ticks, and family-arc
        /// keys are deliberately absent from this list and therefore never become required prompt fields.
        /// </summary>
        private static bool IsRequiredBiotechContextKey(string contextKey)
        {
            return Eq(contextKey, "birthday_age")
                || Eq(contextKey, "opportunity_description")
                || Eq(contextKey, "selected_trait")
                || StartsWithAny(contextKey, "new_interest_", "interest_change_")
                || Eq(contextKey, "previous_name")
                || Eq(contextKey, "current_name")
                || Eq(contextKey, "new_responsibilities")
                || Eq(contextKey, "supporter_role")
                || Eq(contextKey, "initiator_family_role")
                || Eq(contextKey, "recipient_family_role")
                || Eq(contextKey, "child_name")
                || Eq(contextKey, "birth_outcome")
                || Eq(contextKey, "birth_method")
                || Eq(contextKey, "birther_died")
                || Eq(contextKey, "ritual_birth");
        }

        /// <summary>
        /// Keeps the minimum truth needed to write a gravship landing in every detail preset.
        /// An exact landing outcome is also required when supplied because the group instruction asks
        /// the writer to use that visible consequence. Remaining Odyssey fields (crew roster,
        /// biome/site labels, launch quality, and roughness)
        /// are useful supporting evidence, but Compact may trim them before it trims the journey's
        /// reason, duration, ship, origin, destination, or a solo writer's journey role.
        /// </summary>
        private static bool IsRequiredOdysseyContextKey(string contextKey)
        {
            return Eq(contextKey, "journey_phase")
                || Eq(contextKey, "journey_reason")
                || Eq(contextKey, "journey_secondary_reason")
                || Eq(contextKey, "journey_duration")
                || Eq(contextKey, "pov_journey_role")
                || Eq(contextKey, "ship_name")
                || Eq(contextKey, "origin")
                || Eq(contextKey, "destination")
                || Eq(contextKey, "landing_outcome");
        }

        /// <summary>
        /// Keeps persona identity and the exact lifecycle edge intelligible in Compact prompts.
        /// Internal Thing IDs and epoch counters remain optional implementation metadata.
        /// </summary>
        private static bool IsRequiredRoyaltyContextKey(string contextKey)
        {
            return Eq(contextKey, "persona_weapon_name")
                // Phase 2 standalone bond pages may emit persona_weapon; Phase 3 Tale/death pages
                // intentionally carry the bond facts as namespaced gameContext keys instead.
                || Eq(contextKey, "persona_weapon")
                || Eq(contextKey, "persona_milestone")
                || Eq(contextKey, "tale_source_def")
                || Eq(contextKey, "tale_source_label")
                || Eq(contextKey, "tale_killer_role")
                || Eq(contextKey, "tale_victim_role")
                || Eq(contextKey, "bond_previous_state")
                || Eq(contextKey, "bond_new_state")
                || Eq(contextKey, "bond_separation_duration")
                || Eq(contextKey, "bond_duration")
                || Eq(contextKey, "bond_previous_pawn")
                || Eq(contextKey, "bond_end_cause")
                // R4 mutation identity and exact before/after truth stay visible in Compact. The
                // optional royal_duty_changes field is deliberately absent and gets budgeted first.
                || Eq(contextKey, "royal_mutation_pawn")
                || Eq(contextKey, "royal_cause")
                || Eq(contextKey, "royal_transition")
                || Eq(contextKey, "royal_faction")
                || Eq(contextKey, "previous_title")
                || Eq(contextKey, "title")
                || Eq(contextKey, "previous_psylink_level")
                || Eq(contextKey, "psylink_level")
                || Eq(contextKey, "psylink_cause")
                // R5 succession pages are unintelligible if any supplied identity/title edge is cut.
                || Eq(contextKey, "succession_deceased")
                || Eq(contextKey, "succession_heir")
                || Eq(contextKey, "succession_title")
                || Eq(contextKey, "succession_faction")
                // R6 permit identity and authority stay exact in every detail preset. Setting is
                // useful color but remains optional; cooldown use becomes required only when present.
                || Eq(contextKey, "permit_label")
                || Eq(contextKey, "permit_family")
                || Eq(contextKey, "permit_faction")
                || Eq(contextKey, "permit_title")
                || Eq(contextKey, "used_during_cooldown");
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
            bool persona = DomainOrContext(domain, gameContext, "PersonaWeapon")
                || HasAnyMarker(gameContext, "persona_weapon=", "persona_milestone=");
            bool social = HasAnyMarker(gameContext, "worker=Interaction_", "romance=", "kind=married", "def=Insult");
            bool thought = DomainOrContext(domain, gameContext, "Thought")
                || HasAnyMarker(gameContext, "thought=", "thought_def=");
            bool criticalBelief = HasAnyMarker(gameContext,
                "belief_event=conversion", "belief_event=crisis", "belief_crisis=",
                "conversion_ritual=", "mental_state=IdeoChange");

            if (Eq(source, BeliefContextPrompt.Source))
            {
                if (criticalBelief)
                {
                    reason = "conversion or belief-crisis context";
                    return 94;
                }
                if (thought)
                {
                    reason = "ordinary thought belief context";
                    return 68;
                }
                if (social)
                {
                    reason = "ordinary social belief context";
                    return 72;
                }
                reason = "event-relative belief context";
                return 76;
            }

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
                return ScoreContextKey(contextKey, quest, progression, ability, ritual, combat, persona, level, out reason);
            }

            reason = "optional context";
            return 40;
        }

        private static int ScoreContextKey(string contextKey, bool quest, bool progression, bool ability,
            bool ritual, bool combat, bool persona, PromptContextDetailLevel level, out string reason)
        {
            if (StartsWithAny(contextKey, "permit_") || Eq(contextKey, "used_during_cooldown"))
            {
                reason = Eq(contextKey, "permit_setting")
                    ? "optional permit setting"
                    : "exact dramatic permit fact";
                return Eq(contextKey, "permit_setting") ? 64 : 92;
            }

            if (StartsWithAny(contextKey, "persona_", "bond_"))
            {
                reason = persona ? "persona lifecycle context" : "persona context";
                return persona ? 86 : 48;
            }

            if (Eq(contextKey, "royal_duty_changes"))
            {
                reason = "optional royal duty color";
                return level == PromptContextDetailLevel.Compact ? 6 : 24;
            }

            if (StartsWithAny(contextKey, "succession_"))
            {
                reason = "exact royal succession fact";
                return 92;
            }

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

        private static int BudgetFor(string templateKey, PromptContextDetailLevel level, PromptContextBudgets budgets)
        {
            if (level == PromptContextDetailLevel.Full)
            {
                return FullBudget;
            }

            PromptContextBudgets b = budgets ?? PromptContextBudgets.Defaults;
            bool reflection = Eq(templateKey, DiaryPipelineTemplates.SoloDayReflection)
                || Eq(templateKey, DiaryPipelineTemplates.SoloQuadrumReflection)
                || Eq(templateKey, DiaryPipelineTemplates.SoloArcReflection);
            bool neutral = Eq(templateKey, DiaryPipelineTemplates.DeathDescription)
                || Eq(templateKey, DiaryPipelineTemplates.ArrivalDescription);
            if (level == PromptContextDetailLevel.Balanced)
            {
                return reflection ? b.balancedReflection : neutral ? b.balancedNeutral : b.balancedDefault;
            }

            return reflection ? b.compactReflection : neutral ? b.compactNeutral : b.compactDefault;
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

            // Match PromptAssembler.AppendField exactly (ordinal, case-sensitive) so Full stays a
            // faithful pass-through: a value that the renderer would keep must never be dropped here.
            string trimmed = value.Trim();
            return trimmed != "none" && trimmed != "n/a" && trimmed != "unknown";
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
