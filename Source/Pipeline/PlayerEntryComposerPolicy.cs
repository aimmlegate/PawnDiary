// Pure contracts and validation for the player entry composer. The UI and game adapter pass plain
// snapshots through this file; it has no Verse, Unity, DefDatabase, settings, transport, or save-state
// dependency. In particular, generated text is always a transient review draft until the player uses
// the ordinary manual-entry Save operation.
using System;
using System.Collections.Generic;
using System.Text;

namespace PawnDiary
{
    /// <summary>The four visible stages of the review-first player composer.</summary>
    internal enum PlayerEntryComposerMode
    {
        Direct,
        Context,
        FullPrompt,
        Review
    }

    /// <summary>Transient one-shot completion state exposed only to the in-game composer.</summary>
    internal enum PlayerEntryDraftStatus
    {
        Unknown,
        Pending,
        Succeeded,
        Failed
    }

    /// <summary>Immediate admission result from the component-owned draft facade.</summary>
    internal sealed class PlayerEntryDraftStartResult
    {
        public bool accepted;
        public int handle;
        public string errorCode = string.Empty;
        public string entryTypeKey = string.Empty;
        public string templateKey = string.Empty;
    }

    /// <summary>Detached poll result. A successful draft returns body text only, never a title.</summary>
    internal sealed class PlayerEntryDraftPollResult
    {
        public PlayerEntryDraftStatus status = PlayerEntryDraftStatus.Unknown;
        public string text = string.Empty;
        public string error = string.Empty;
    }

    /// <summary>Detached, localized display and prompt policy for one player-selectable entry type.</summary>
    internal sealed class PlayerEntryTypeSnapshot
    {
        public string entryTypeKey = string.Empty;
        public string eventPromptKey = string.Empty;
        public string defaultTemplateKey = string.Empty;
        public int displayOrder;
        public bool important;
        public bool combat;
        public bool reflection;
        public string colorCue = string.Empty;
        public string domain = string.Empty;
        public string label = string.Empty;
        public string description = string.Empty;
    }

    /// <summary>Detached, localized view of one XML-opted prompt template.</summary>
    internal sealed class PlayerEntryTemplateSnapshot
    {
        public string templateKey = string.Empty;
        public int displayOrder;
        public string label = string.Empty;
        public string description = string.Empty;
    }

    /// <summary>Plain generation-draft input shared by validation, the runtime facade, and tests.</summary>
    internal sealed class PlayerEntryComposerRequest
    {
        public PlayerEntryComposerMode mode;
        public string entryTypeKey = string.Empty;
        public string templateKey = string.Empty;
        public string factualSummary = string.Empty;
        public string customInstruction = string.Empty;
        public string systemPrompt = string.Empty;
        public string userPrompt = string.Empty;
        public int laneIndex = -1;
        public int maxTokens = PlayerEntryComposerPolicy.UseTemplateOrSettingsMaxTokens;
    }

    /// <summary>Sanitized generation-draft plan. ErrorCode is a stable internal token, never UI copy.</summary>
    internal sealed class PlayerEntryComposerPlan
    {
        public bool valid;
        public string errorCode = string.Empty;
        public PlayerEntryComposerMode mode;
        public string entryTypeKey = string.Empty;
        public string templateKey = string.Empty;
        public string factualSummary = string.Empty;
        public string customInstruction = string.Empty;
        public string systemPrompt = string.Empty;
        public string userPrompt = string.Empty;
        public int laneIndex = -1;
        public int maxTokens;
    }

    /// <summary>
    /// Plain per-POV meaning used by hot events, compact archive rows, prompt policy, and public reads.
    /// A player category overrides source-derived meaning only for the selected POV.
    /// </summary>
    internal sealed class PlayerEntrySemanticProjection
    {
        public string entryTypeKey = string.Empty;
        public string domain = string.Empty;
        public string label = string.Empty;
        public string colorCue = string.Empty;
        public bool important;
        public bool combat;
        public bool reflection;
    }

    /// <summary>Pure category overlay shared by hot and archived entry projections.</summary>
    internal static class PlayerEntrySemanticPolicy
    {
        public static PlayerEntrySemanticProjection Project(
            PlayerEntryTypeSnapshot playerType,
            string sourceDomain,
            string sourceLabel,
            string sourceColorCue,
            bool sourceImportant,
            bool sourceCombat,
            bool sourceReflection)
        {
            if (playerType == null)
            {
                return new PlayerEntrySemanticProjection
                {
                    domain = Clean(sourceDomain),
                    label = sourceLabel ?? string.Empty,
                    colorCue = sourceColorCue ?? string.Empty,
                    important = sourceImportant,
                    combat = sourceCombat,
                    reflection = sourceReflection
                };
            }

            return new PlayerEntrySemanticProjection
            {
                entryTypeKey = Clean(playerType.entryTypeKey),
                // Player-authored facts do not prove a raid, quest, hediff, thought, or generated
                // reflection occurred. Even a malformed old/custom category cannot opt back into a
                // capture domain: the category axis carries its finer meaning separately.
                domain = DiaryEventDomainClassifier.PlayerEntry,
                label = playerType.label ?? string.Empty,
                colorCue = playerType.colorCue ?? string.Empty,
                important = playerType.important,
                combat = playerType.combat,
                reflection = playerType.reflection
            };
        }

        private static string Clean(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }
    }

    /// <summary>Plain create/edit input for final player-authored prose.</summary>
    internal sealed class PlayerEntryMutationRequest
    {
        public bool creating;
        public bool entryTypeLocked;
        public string originalTitle = string.Empty;
        public string originalBody = string.Empty;
        public string originalEntryTypeKey = string.Empty;
        public string requestedTitle = string.Empty;
        public string requestedBody = string.Empty;
        public string requestedEntryTypeKey = string.Empty;
        public int titleMaxCharacters;
        public int bodyMaxCharacters;
    }

    /// <summary>Normalized final text and category mutation decision.</summary>
    internal sealed class PlayerEntryMutationPlan
    {
        public bool valid;
        public string errorCode = string.Empty;
        public string title = string.Empty;
        public string body = string.Empty;
        public string entryTypeKey = string.Empty;
        public bool textChanged;
        public bool typeChanged;
        public bool noChange;
    }

    /// <summary>
    /// Pure final-save boundary. UI and persistence supply the same XML-snapshotted caps and detached
    /// category catalog, so Direct and generated Review saves cannot validate one contract and persist
    /// another. Unchanged legacy fields remain byte-for-byte intact even when they exceed today's caps.
    /// </summary>
    internal static class PlayerEntryMutationPolicy
    {
        public static PlayerEntryMutationPlan Plan(
            PlayerEntryMutationRequest request,
            IList<PlayerEntryTypeSnapshot> entryTypes)
        {
            PlayerEntryMutationPlan result = new PlayerEntryMutationPlan();
            if (request == null)
            {
                result.errorCode = "missing_request";
                return result;
            }

            if (request.titleMaxCharacters <= 0 || request.bodyMaxCharacters <= 0)
            {
                result.errorCode = "invalid_caps";
                return result;
            }

            string originalTitle = request.originalTitle ?? string.Empty;
            string originalBody = request.originalBody ?? string.Empty;
            string originalType = request.originalEntryTypeKey ?? string.Empty;
            string requestedTitle = request.requestedTitle ?? string.Empty;
            string requestedBody = request.requestedBody ?? string.Empty;
            string requestedType = CleanKey(request.requestedEntryTypeKey);

            bool bodyChanged = request.creating
                || !string.Equals(requestedBody, originalBody, StringComparison.Ordinal);
            bool titleChanged = request.creating
                || !string.Equals(requestedTitle, originalTitle, StringComparison.Ordinal);
            result.body = bodyChanged
                ? ExternalDirectEntryText.CleanProse(requestedBody, 0)
                : originalBody;
            result.title = titleChanged
                ? ExternalDirectEntryText.CleanTitle(requestedTitle, 0)
                : originalTitle;

            if (string.IsNullOrWhiteSpace(result.body))
            {
                result.errorCode = "blank_body";
                return result;
            }

            if ((bodyChanged && result.body.Length > request.bodyMaxCharacters)
                || (titleChanged && result.title.Length > request.titleMaxCharacters))
            {
                result.errorCode = "text_too_long";
                return result;
            }

            if (request.creating)
            {
                PlayerEntryTypeSnapshot selected = PlayerEntryComposerPolicy.ResolveEntryType(
                    requestedType, entryTypes);
                if (selected == null)
                {
                    result.errorCode = "unknown_entry_type";
                    return result;
                }

                result.entryTypeKey = selected.entryTypeKey ?? string.Empty;
                result.typeChanged = true;
            }
            else
            {
                result.typeChanged = !string.Equals(
                    requestedType, originalType, StringComparison.Ordinal);
                if (!result.typeChanged)
                {
                    // A blank/unknown legacy key is legal while unchanged.
                    result.entryTypeKey = originalType;
                }
                else
                {
                    if (request.entryTypeLocked)
                    {
                        result.errorCode = "entry_type_locked";
                        return result;
                    }

                    PlayerEntryTypeSnapshot selected = requestedType.Length == 0
                        ? null
                        : PlayerEntryComposerPolicy.ResolveEntryType(requestedType, entryTypes);
                    if (selected == null)
                    {
                        result.errorCode = "unknown_entry_type";
                        return result;
                    }

                    result.entryTypeKey = selected.entryTypeKey ?? string.Empty;
                }
            }

            result.textChanged = bodyChanged || titleChanged;
            result.noChange = !request.creating && !result.textChanged && !result.typeChanged;
            result.valid = true;
            return result;
        }

        private static string CleanKey(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }
    }

    /// <summary>Pure validation, defensive caps, and template allow-list rules for composer input.</summary>
    internal static class PlayerEntryComposerPolicy
    {
        public const string PersonalEntryTypeKey = "Personal";
        public const int ContextSummaryMaxCharacters = 4000;
        public const int ContextInstructionMaxCharacters = 4000;
        public const int RawPromptMaxCharacters = 4000;
        public const int MinMaxTokens = 16;
        // Public one-shot adapters stop at 600; trusted XML/global policy may use the same 4,000-token
        // ceiling exposed by Prompt Studio's template field without letting corrupt values run unbounded.
        public const int MaxMaxTokens = 600;
        public const int MaxResolvedPolicyTokens = 4000;
        // Zero is a policy sentinel: Context mode uses the selected XML template's positive cap,
        // then both generating modes fall back to the player's global setting when no cap exists.
        public const int UseTemplateOrSettingsMaxTokens = 0;

        /// <summary>
        /// Produces a detached validated plan. Raw prompts preserve every character except NUL and
        /// disallowed control characters, then apply a Unicode-safe prefix cap; spaces and newlines are
        /// otherwise byte-for-byte unchanged.
        /// </summary>
        public static PlayerEntryComposerPlan Plan(
            PlayerEntryComposerRequest request,
            IList<PlayerEntryTypeSnapshot> entryTypes,
            IList<PlayerEntryTemplateSnapshot> templates)
        {
            PlayerEntryComposerPlan result = new PlayerEntryComposerPlan();
            if (request == null)
            {
                result.errorCode = "missing_request";
                return result;
            }

            result.mode = request.mode;
            result.laneIndex = request.laneIndex;
            result.maxTokens = NormalizeRequestedMaxTokens(request.maxTokens);
            result.factualSummary = CleanProse(
                request.factualSummary, ContextSummaryMaxCharacters).Trim();
            result.customInstruction = CleanProse(
                request.customInstruction, ContextInstructionMaxCharacters).Trim();
            result.systemPrompt = CleanRawPrompt(request.systemPrompt, RawPromptMaxCharacters);
            result.userPrompt = CleanRawPrompt(request.userPrompt, RawPromptMaxCharacters);

            PlayerEntryTypeSnapshot entryType = ResolveEntryType(request.entryTypeKey, entryTypes);
            if (entryType == null)
            {
                result.errorCode = "unknown_entry_type";
                return result;
            }

            result.entryTypeKey = entryType.entryTypeKey;
            result.templateKey = string.Empty;

            if (request.mode == PlayerEntryComposerMode.Direct
                || request.mode == PlayerEntryComposerMode.Review)
            {
                // Final prose crosses PlayerEntryMutationPolicy with XML-snapshotted title/body caps.
                // This policy admits generation drafts only, so it cannot become a second save contract.
                result.errorCode = "mode_not_generating";
                return result;
            }
            else if (request.mode == PlayerEntryComposerMode.Context)
            {
                result.templateKey = ResolveTemplateKey(
                    request.templateKey,
                    entryType.defaultTemplateKey,
                    templates);
                if (string.IsNullOrWhiteSpace(result.templateKey))
                {
                    result.errorCode = "unknown_template";
                    return result;
                }

                if (string.IsNullOrWhiteSpace(result.factualSummary)
                    && string.IsNullOrWhiteSpace(result.customInstruction))
                {
                    result.errorCode = "blank_context_request";
                    return result;
                }
            }
            else if (request.mode == PlayerEntryComposerMode.FullPrompt)
            {
                if (string.IsNullOrWhiteSpace(result.userPrompt))
                {
                    result.errorCode = "blank_user_prompt";
                    return result;
                }
            }
            else
            {
                result.errorCode = "unknown_mode";
                return result;
            }

            result.valid = true;
            return result;
        }

        /// <summary>Returns the exact key only when it appears in the detached selectable catalog.</summary>
        public static string ResolveTemplateKey(
            string requestedKey,
            string defaultKey,
            IList<PlayerEntryTemplateSnapshot> templates)
        {
            string requested = CleanKey(requestedKey);
            string fallback = CleanKey(defaultKey);
            if (templates == null)
            {
                return string.Empty;
            }

            if (ContainsTemplate(templates, requested)) return requested;
            return ContainsTemplate(templates, fallback) ? fallback : string.Empty;
        }

        /// <summary>
        /// Resolves a planner/template token cap against the player's global setting. Positive values
        /// win; zero means "inherit". The final value stays inside the one-shot transport's defensive
        /// response band.
        /// </summary>
        public static int ResolveCompletionMaxTokens(int plannedMaxTokens, int settingsMaxTokens)
        {
            int chosen = plannedMaxTokens > 0 ? plannedMaxTokens : settingsMaxTokens;
            return Clamp(chosen, MinMaxTokens, MaxResolvedPolicyTokens);
        }

        /// <summary>Finds an exact entry type; blank input intentionally selects Personal.</summary>
        public static PlayerEntryTypeSnapshot ResolveEntryType(
            string requestedKey,
            IList<PlayerEntryTypeSnapshot> entryTypes)
        {
            if (entryTypes == null || entryTypes.Count == 0)
            {
                return null;
            }

            string key = CleanKey(requestedKey);
            bool usePersonalFallback = key.Length == 0;
            if (usePersonalFallback) key = PersonalEntryTypeKey;
            PlayerEntryTypeSnapshot fallback = null;
            for (int i = 0; i < entryTypes.Count; i++)
            {
                PlayerEntryTypeSnapshot row = entryTypes[i];
                if (row == null) continue;
                if (string.Equals(row.entryTypeKey, PersonalEntryTypeKey,
                    StringComparison.OrdinalIgnoreCase)) fallback = row;
                if (string.Equals(row.entryTypeKey, key, StringComparison.OrdinalIgnoreCase))
                {
                    return row;
                }
            }

            return usePersonalFallback ? fallback : null;
        }

        /// <summary>Planner-side allow-list predicate for a solo, XML-opted template snapshot.</summary>
        public static bool IsRequestedTemplateAllowed(
            string requestedKey,
            bool solo,
            string candidateKey,
            bool playerSelectable)
        {
            return solo
                && playerSelectable
                && !string.IsNullOrWhiteSpace(requestedKey)
                && string.Equals(requestedKey.Trim(), candidateKey,
                    StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Raw-prompt sanitation contract used before a one-shot completion.</summary>
        public static string CleanRawPrompt(string value, int maxCharacters)
        {
            if (string.IsNullOrEmpty(value) || maxCharacters <= 0) return string.Empty;
            StringBuilder builder = null;
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                bool allowed = c == '\r' || c == '\n' || c == '\t' || !char.IsControl(c);
                if (allowed) continue;
                if (builder == null)
                {
                    builder = new StringBuilder(value.Length);
                    builder.Append(value, 0, i);
                }
                // Preserve offsets without inventing prompt text: prohibited control bytes disappear.
            }

            if (builder != null)
            {
                int copiedUntil = builder.Length;
                for (int i = copiedUntil; i < value.Length; i++)
                {
                    char c = value[i];
                    if (c == '\r' || c == '\n' || c == '\t' || !char.IsControl(c)) builder.Append(c);
                }
                value = builder.ToString();
            }

            return SafePrefix(value, maxCharacters);
        }

        private static string CleanProse(string value, int maxCharacters)
        {
            return CleanRawPrompt(value, maxCharacters);
        }

        private static bool ContainsTemplate(IList<PlayerEntryTemplateSnapshot> templates, string key)
        {
            if (key.Length == 0) return false;
            for (int i = 0; i < templates.Count; i++)
            {
                if (string.Equals(templates[i]?.templateKey, key, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static string CleanKey(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }

        private static int NormalizeRequestedMaxTokens(int value)
        {
            return value <= 0
                ? UseTemplateOrSettingsMaxTokens
                : Clamp(value, MinMaxTokens, MaxMaxTokens);
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min) return min;
            return value > max ? max : value;
        }

        private static string SafePrefix(string value, int maxCharacters)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= maxCharacters) return value ?? string.Empty;
            int length = maxCharacters;
            if (length > 0 && char.IsHighSurrogate(value[length - 1])) length--;
            return length <= 0 ? string.Empty : value.Substring(0, length);
        }
    }
}
