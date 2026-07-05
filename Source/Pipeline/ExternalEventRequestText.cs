// Pure cleanup for ordinary external event requests. Both live submissions and prompt previews use
// this so adapter-supplied summary/context/prompt-fragment text has one set of caps and one
// sanitation rule.
//
// New to C#/RimWorld? See AGENTS.md.
using System;
using System.Collections.Generic;

namespace PawnDiary
{
    /// <summary>
    /// Sanitizes text fields supplied through <c>ExternalEventRequest</c>. Internal because this is
    /// implementation detail of the integration adapter layer; the public contract lives in
    /// <c>PawnDiary.Integration</c>. The standalone <c>DiaryPipelineTests</c> project reaches in via
    /// <c>[InternalsVisibleTo]</c> in <c>Source/Properties/AssemblyInfo.cs</c>.
    /// </summary>
    internal static class ExternalEventRequestText
    {
        public const int MaxExtraContextLines = 16;
        public const int MaxExtraContextLineChars = 200;
        public const int MaxSummaryChars = 800;
        public const int MaxPromptInstructionChars = 2000;

        // Absolute defensive ceiling on the number of "key=value" fields one external request can write
        // into saved gameContext. Per-source caps (MaxExtraContextLines, the XML-tuned enchantment
        // candidate cap) bound the common case, but if a tuning author raises the enchantment candidate
        // cap the protected-field block would grow without a code-level backstop. This mirrors the
        // MaxListeners/MaxProviders parser-limit pattern: a schema safety limit, not feature policy.
        public const int MaxRequestContextLines = 64;
        public const string PromptInstructionContextKey = "external_prompt_instruction";
        public const string PromptFragmentContextKey = "external_prompt_fragment";
        public const string PromptEnchantmentModeContextKey = "external_prompt_enchantment_mode";
        public const string PromptEnchantmentReplaceMode = "replace";
        public const string PromptEnchantmentContextKeyPrefix = "external_prompt_enchantment_";

        /// <summary>
        /// One-lines and length-caps the adapter's summary so a stray multi-paragraph submission
        /// cannot distort the prompt or diary card's raw-text row.
        /// </summary>
        public static string CleanSummary(string summary)
        {
            string cleaned = PromptTextSanitizer.OneLine(summary);
            return TextTruncation.SafePrefix(cleaned, MaxSummaryChars);
        }

        /// <summary>
        /// Sanitizes and joins adapter "key=value" context lines into the semicolon-separated game
        /// context shape used by diary prompts.
        /// </summary>
        public static string JoinExtraContext(List<string> lines)
        {
            return PromptContextLines.Join(lines, MaxExtraContextLines, MaxExtraContextLineChars);
        }

        /// <summary>
        /// Sanitizes a caller-supplied prompt fragment. It is stored as a normal game-context field,
        /// so semicolons are flattened before joining to avoid creating accidental extra fields.
        /// </summary>
        public static string CleanPromptFragment(string promptFragment, int maxChars)
        {
            return PromptContextLines.CleanLine(promptFragment, maxChars);
        }

        /// <summary>
        /// Sanitizes the wrapped prompt-entry instruction. The instruction is still one protected
        /// context field, so semicolons and line breaks are flattened before it reaches templates.
        /// </summary>
        public static string CleanPromptInstruction(string promptInstruction)
        {
            return PromptContextLines.CleanLine(promptInstruction, MaxPromptInstructionChars);
        }

        /// <summary>
        /// Sanitizes one caller-supplied prompt-enchantment candidate line.
        /// </summary>
        public static string CleanPromptEnchantmentCandidate(string candidate, int maxChars)
        {
            return PromptContextLines.CleanLine(candidate, maxChars);
        }

        /// <summary>
        /// Sanitizes and caps the caller-supplied prompt-enchantment candidate list.
        /// </summary>
        public static List<string> CleanPromptEnchantmentCandidates(
            IEnumerable<string> candidates,
            int maxCount,
            int maxChars)
        {
            List<string> cleaned = new List<string>();
            if (candidates == null || maxCount <= 0)
            {
                return cleaned;
            }

            foreach (string candidate in candidates)
            {
                string line = CleanPromptEnchantmentCandidate(candidate, maxChars);
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                cleaned.Add(line);
                if (cleaned.Count >= maxCount)
                {
                    break;
                }
            }

            return cleaned;
        }

        /// <summary>
        /// Joins protected v16 prompt fields before ordinary adapter extraContext. Because
        /// DiaryContextFields.Value returns the first matching key, adapter-supplied duplicates later
        /// in extraContext cannot override these API-owned fields.
        /// </summary>
        public static string JoinRequestContext(
            string promptInstruction,
            string promptFragment,
            IEnumerable<string> promptEnchantmentCandidates,
            bool replacePromptEnchantments,
            List<string> extraContext,
            int promptFragmentMaxChars,
            int promptEnchantmentMaxCandidates,
            int promptEnchantmentCandidateMaxChars)
        {
            List<string> protectedFields = new List<string>();

            string instruction = CleanPromptInstruction(promptInstruction);
            if (!string.IsNullOrWhiteSpace(instruction))
            {
                protectedFields.Add(PromptInstructionContextKey + "=" + instruction);
            }

            string fragment = CleanPromptFragment(promptFragment, promptFragmentMaxChars);
            if (!string.IsNullOrWhiteSpace(fragment))
            {
                protectedFields.Add(PromptFragmentContextKey + "=" + fragment);
            }

            List<string> candidates = CleanPromptEnchantmentCandidates(
                promptEnchantmentCandidates,
                promptEnchantmentMaxCandidates,
                promptEnchantmentCandidateMaxChars);
            if (candidates.Count > 0 && replacePromptEnchantments)
            {
                protectedFields.Add(PromptEnchantmentModeContextKey + "=" + PromptEnchantmentReplaceMode);
            }

            // Bound the protected-field block so the combined saved gameContext can never exceed
            // MaxRequestContextLines even if a tuning author raises the XML enchantment candidate cap.
            // The instruction and fragment (when present) plus the mode marker already occupy slots;
            // the rest are enchantment candidates. Any overflow is dropped here, before the join.
            int reservedHeadroom = protectedFields.Count;
            int maxCandidateSlots = Math.Max(0, MaxRequestContextLines - reservedHeadroom);
            int candidateCount = Math.Min(candidates.Count, maxCandidateSlots);
            for (int i = 0; i < candidateCount; i++)
            {
                protectedFields.Add(PromptEnchantmentContextKeyPrefix + (i + 1) + "=" + candidates[i]);
            }

            string protectedContext = PromptContextLines.Join(
                protectedFields,
                Math.Min(protectedFields.Count, MaxRequestContextLines),
                int.MaxValue);
            string ordinaryContext = JoinAdapterExtraContext(extraContext);
            if (string.IsNullOrEmpty(protectedContext))
            {
                return ordinaryContext;
            }

            if (string.IsNullOrEmpty(ordinaryContext))
            {
                return protectedContext;
            }

            return protectedContext + "; " + ordinaryContext;
        }

        /// <summary>
        /// Converts saved v16 context fields back into pure prompt-enchantment candidates.
        /// </summary>
        public static List<PromptEnchantmentCandidate> PromptEnchantmentCandidatesFromContext(
            string context,
            int maxCandidates,
            float weight)
        {
            List<PromptEnchantmentCandidate> candidates = new List<PromptEnchantmentCandidate>();
            if (string.IsNullOrWhiteSpace(context) || maxCandidates <= 0
                || weight <= 0f || float.IsNaN(weight))
            {
                return candidates;
            }

            for (int i = 1; i <= maxCandidates; i++)
            {
                string value = DiaryContextFields.Value(context, PromptEnchantmentContextKeyPrefix + i);
                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                candidates.Add(new PromptEnchantmentCandidate
                {
                    conditionText = value,
                    weight = weight
                });
            }

            return candidates;
        }

        /// <summary>
        /// Returns true when saved context asks external candidates to replace ordinary live
        /// prompt-enchantment candidates.
        /// </summary>
        public static bool PromptEnchantmentsReplaceNormal(string context)
        {
            return DiaryContextFields.FieldEquals(
                context,
                PromptEnchantmentModeContextKey,
                PromptEnchantmentReplaceMode);
        }

        /// <summary>
        /// Cleans, caps, and joins an adapter's ordinary "key=value" extraContext lines, dropping any
        /// line whose key is a reserved internal game-context key (see <see cref="IsReservedContextKey"/>).
        /// Shared by the event path (via <see cref="JoinRequestContext"/>) and the direct-entry path so
        /// neither can let adapter input forge an internal structural or prompt field.
        /// </summary>
        internal static string JoinAdapterExtraContext(IEnumerable<string> lines)
        {
            List<string> kept = new List<string>();
            if (lines == null)
            {
                return string.Empty;
            }

            foreach (string rawLine in lines)
            {
                string line = PromptContextLines.CleanLine(rawLine, MaxExtraContextLineChars);
                if (string.IsNullOrWhiteSpace(line) || IsReservedContextLine(line))
                {
                    continue;
                }

                kept.Add(line);
                if (kept.Count >= MaxExtraContextLines)
                {
                    break;
                }
            }

            return PromptContextLines.Join(kept, kept.Count, int.MaxValue);
        }

        private static bool IsReservedContextLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                return false;
            }

            int equalsIndex = line.IndexOf('=');
            if (equalsIndex <= 0)
            {
                return false;
            }

            return IsReservedContextKey(line.Substring(0, equalsIndex));
        }

        /// <summary>
        /// True when <paramref name="key"/> is a game-context key that Pawn Diary's own pipeline reads
        /// to drive event-domain classification, POV selection, death/arrival/reflection rendering, or a
        /// structured prompt field. Adapter-supplied extraContext must never set one of these: because
        /// <see cref="DiaryContextFields.Value"/> is first-match, a smuggled structural key (e.g.
        /// "death_description=true") would force a death/neutral page or inject prompt content the API
        /// did not sanction. Free-form adapter keys (location, weather, mood, ...) are unaffected.
        ///
        /// This is the one place that owns the reserved set: when a new internal game-context key is
        /// added anywhere in the codebase, register it (or its domain prefix) here.
        /// </summary>
        internal static bool IsReservedContextKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return false;
            }

            string trimmed = key.Trim();
            if (ReservedContextKeys.Contains(trimmed))
            {
                return true;
            }

            for (int i = 0; i < ReservedContextKeyPrefixes.Length; i++)
            {
                if (trimmed.StartsWith(ReservedContextKeyPrefixes[i], StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        // Exact reserved keys that have no distinguishing domain prefix. Domain-prefixed keys
        // (death_*, arrival_*, quest_*, ability_*, external_*, ...) are covered by ReservedContextKeyPrefixes
        // below, which also future-proofs new sub-keys in those domains.
        private static readonly HashSet<string> ReservedContextKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // Event-domain markers — Source/Pipeline/DiaryEventDomainClassifier.cs
            "external", "tale", "mood_event", "thought", "inspiration", "romance", "work", "hediff",
            "mental_state", "raid", "quest", "ritual", "psychic_ritual", "ability", "progression",
            // Classifier value keys
            "signal", "part_kind",
            // Attribution — Source/Pipeline/ExternalEntryAttribution.cs
            "source",
            // Prompt ContextField keys with no reserved prefix — Source/Defs/PromptArchitectureDefs.cs
            "quadrum", "important_entries",
        };

        // Reserved key families: any adapter key inside one of Pawn Diary's structured domains is
        // rejected even if a new sub-key is added later. "external_" also subsumes the protected prompt
        // fields (external_prompt_instruction / _fragment / _enchantment_mode / _enchantment_N).
        private static readonly string[] ReservedContextKeyPrefixes =
        {
            "external_", "death_", "arrival_", "quest_", "ability_", "ritual_", "royal_",
            "quadrum_", "arc_", "day_", "mood_", "mental_", "psychic_", "ideolog",
        };
    }
}
