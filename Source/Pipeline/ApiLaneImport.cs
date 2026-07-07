// Pure token<->enum mapping and validation for the public "read / add API lane" integration surface.
// The public DTOs deliberately speak in stable STRING TOKENS ("bearer", "chatCompletions",
// "balanced", ...) instead of the internal ApiAuthMode / ApiCompatibilityMode / ApiLaneRoutingMode /
// PromptContextDetailOverride enums, so the contract other mods compile against never breaks if those
// internal enums are renamed or reordered. This file is the single audited place that translates in
// both directions and validates an incoming add-lane request. It stays pure (no RimWorld / Verse /
// Unity / settings / IO) so tests can cover the mapping and reuses ApiEndpointPolicy for the actual
// normalization rules the runtime already trusts.
//
// New to C#/RimWorld? See AGENTS.md.
using System;

namespace PawnDiary
{
    /// <summary>
    /// Pure helpers that map API-lane enum values to and from the stable string tokens used by the
    /// public integration DTOs, and validate an add-lane request. Never depends on the game runtime.
    /// </summary>
    internal static class ApiLaneImport
    {
        // --- Auth-mode tokens (the public "authMode" string on lane DTOs / requests) ---
        public const string AuthBearer = "bearer";
        public const string AuthNone = "none";
        public const string AuthCustomHeader = "customHeader";
        public const string AuthQueryParam = "queryParam";

        // --- Compatibility-mode tokens (the public "apiMode" string) ---
        public const string ApiModeChat = "chatCompletions";
        public const string ApiModeResponses = "responses";

        // --- Routing-mode tokens (the public "routingMode" string on the setup DTO) ---
        public const string RoutingBalanced = "balanced";
        public const string RoutingPreferTop = "preferTopRows";
        public const string RoutingFailoverOnly = "failoverOnly";

        // --- Per-lane context-detail override tokens ---
        public const string ContextInherit = "inherit";
        public const string ContextFull = "full";
        public const string ContextBalanced = "balanced";
        public const string ContextCompact = "compact";

        // --- Validation outcome tokens (also surface in AddApiLaneResult.reason) ---
        public const string ReasonOk = "ok";
        public const string ReasonMissingUrl = "missingUrl";
        public const string ReasonMissingModel = "missingModel";

        /// <summary>Maps a normalized auth mode to its stable public token.</summary>
        public static string AuthModeToken(ApiAuthMode mode)
        {
            switch (ApiEndpointPolicy.NormalizeAuthMode(mode))
            {
                case ApiAuthMode.None:
                    return AuthNone;
                case ApiAuthMode.CustomHeader:
                    return AuthCustomHeader;
                case ApiAuthMode.QueryParameterKey:
                    return AuthQueryParam;
                default:
                    return AuthBearer;
            }
        }

        /// <summary>
        /// Parses a public auth-mode token to the internal enum. Unknown/blank tokens fall back to
        /// Bearer (the historical default). Case-insensitive; a few obvious synonyms are accepted so a
        /// caller does not have to remember the exact casing.
        /// </summary>
        public static ApiAuthMode ParseAuthMode(string token)
        {
            string t = (token ?? string.Empty).Trim().ToLowerInvariant();
            switch (t)
            {
                case "none":
                    return ApiAuthMode.None;
                case "customheader":
                case "custom":
                case "header":
                    return ApiAuthMode.CustomHeader;
                case "queryparam":
                case "queryparameter":
                case "queryparameterkey":
                case "query":
                    return ApiAuthMode.QueryParameterKey;
                default:
                    return ApiAuthMode.BearerToken;
            }
        }

        /// <summary>Maps a normalized compatibility mode to its stable public token.</summary>
        public static string ApiModeToken(ApiCompatibilityMode mode)
        {
            return ApiEndpointPolicy.NormalizeApiMode(mode) == ApiCompatibilityMode.OpenAIResponses
                ? ApiModeResponses
                : ApiModeChat;
        }

        /// <summary>
        /// Parses a public compatibility-mode token. Anything but an explicit "responses" token maps to
        /// the OpenAI chat/completions default, matching NormalizeApiMode's own conservative fallback.
        /// </summary>
        public static ApiCompatibilityMode ParseApiMode(string token)
        {
            string t = (token ?? string.Empty).Trim().ToLowerInvariant();
            switch (t)
            {
                case "responses":
                case "openairesponses":
                case "openai_responses":
                    return ApiCompatibilityMode.OpenAIResponses;
                default:
                    return ApiCompatibilityMode.OpenAIChatCompletions;
            }
        }

        /// <summary>Maps a normalized routing mode to its stable public token.</summary>
        public static string RoutingModeToken(ApiLaneRoutingMode mode)
        {
            switch (ApiLaneSelector.Normalize(mode))
            {
                case ApiLaneRoutingMode.PreferTopRows:
                    return RoutingPreferTop;
                case ApiLaneRoutingMode.FailoverOnly:
                    return RoutingFailoverOnly;
                default:
                    return RoutingBalanced;
            }
        }

        /// <summary>Maps a per-lane context-detail override to its stable public token.</summary>
        public static string ContextDetailOverrideToken(PromptContextDetailOverride value)
        {
            switch (value)
            {
                case PromptContextDetailOverride.Full:
                    return ContextFull;
                case PromptContextDetailOverride.Balanced:
                    return ContextBalanced;
                case PromptContextDetailOverride.Compact:
                    return ContextCompact;
                default:
                    return ContextInherit;
            }
        }

        /// <summary>
        /// Parses a per-lane context-detail override token. Unknown/blank tokens inherit the global
        /// setting, which is the safe default the settings UI itself uses.
        /// </summary>
        public static PromptContextDetailOverride ParseContextDetailOverride(string token)
        {
            string t = (token ?? string.Empty).Trim().ToLowerInvariant();
            switch (t)
            {
                case "full":
                    return PromptContextDetailOverride.Full;
                case "balanced":
                    return PromptContextDetailOverride.Balanced;
                case "compact":
                    return PromptContextDetailOverride.Compact;
                default:
                    return PromptContextDetailOverride.Inherit;
            }
        }

        /// <summary>
        /// Validates the required fields of an add-lane request. A lane is unusable for generation
        /// without both a URL and a model (see PawnDiarySettings.ActiveEndpoints), so those are the
        /// two hard requirements. Returns <see cref="ReasonOk"/> when valid, otherwise the specific
        /// missing-field reason token.
        /// </summary>
        public static string ValidateAddRequest(string url, string model)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return ReasonMissingUrl;
            }

            if (string.IsNullOrWhiteSpace(model))
            {
                return ReasonMissingModel;
            }

            return ReasonOk;
        }
    }
}
