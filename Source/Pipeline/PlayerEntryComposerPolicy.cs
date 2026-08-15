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

    /// <summary>Plain input shared by validation, the runtime draft facade, and standalone tests.</summary>
    internal sealed class PlayerEntryComposerRequest
    {
        public PlayerEntryComposerMode mode;
        public string entryTypeKey = string.Empty;
        public string templateKey = string.Empty;
        public string title = string.Empty;
        public string body = string.Empty;
        public string factualSummary = string.Empty;
        public string customInstruction = string.Empty;
        public string systemPrompt = string.Empty;
        public string userPrompt = string.Empty;
        public int laneIndex = -1;
        public int maxTokens = PlayerEntryComposerPolicy.DefaultMaxTokens;
    }

    /// <summary>Sanitized request plan. ErrorCode is a stable internal token, never UI copy.</summary>
    internal sealed class PlayerEntryComposerPlan
    {
        public bool valid;
        public string errorCode = string.Empty;
        public PlayerEntryComposerMode mode;
        public string entryTypeKey = string.Empty;
        public string templateKey = string.Empty;
        public string title = string.Empty;
        public string body = string.Empty;
        public string factualSummary = string.Empty;
        public string customInstruction = string.Empty;
        public string systemPrompt = string.Empty;
        public string userPrompt = string.Empty;
        public int laneIndex = -1;
        public int maxTokens;
    }

    /// <summary>Pure validation, defensive caps, and template allow-list rules for composer input.</summary>
    internal static class PlayerEntryComposerPolicy
    {
        public const string PersonalEntryTypeKey = "Personal";
        public const int TitleMaxCharacters = 200;
        public const int BodyMaxCharacters = 4000;
        public const int ContextSummaryMaxCharacters = 4000;
        public const int ContextInstructionMaxCharacters = 4000;
        public const int RawPromptMaxCharacters = 4000;
        public const int MinMaxTokens = 16;
        public const int MaxMaxTokens = 600;
        public const int DefaultMaxTokens = 200;

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
            result.maxTokens = Clamp(request.maxTokens, MinMaxTokens, MaxMaxTokens);
            result.title = CleanProse(request.title, TitleMaxCharacters).Trim();
            result.body = CleanProse(request.body, BodyMaxCharacters).Trim();
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
                if (string.IsNullOrWhiteSpace(result.body))
                {
                    result.errorCode = "blank_body";
                    return result;
                }
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
