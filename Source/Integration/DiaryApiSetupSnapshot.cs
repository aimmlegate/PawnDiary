// Public read-only DTO describing the whole current LLM API setup: the global request knobs plus one
// DiaryApiLaneSnapshot per configured endpoint/model lane. Returned by PawnDiaryApi.GetApiSetup so an
// adapter can discover which providers/models the player configured (capability GEN-1 groundwork).
//
// Keep this class plain: fields only, no live RimWorld objects. See DiaryApiLaneSnapshot for the
// per-lane fields and the SECURITY note about the exposed apiKey.
//
// New to C#/RimWorld? See AGENTS.md.
using System.Collections.Generic;

namespace PawnDiary.Integration
{
    /// <summary>
    /// The player's current LLM API configuration, exposed through the public integration API.
    /// </summary>
    public sealed class DiaryApiSetupSnapshot
    {
        /// <summary>Stable routing-mode token: "balanced", "preferTopRows", or "failoverOnly".</summary>
        public string routingMode = string.Empty;

        /// <summary>Global sampling temperature applied to generation requests.</summary>
        public float temperature;

        /// <summary>Global per-request timeout in seconds.</summary>
        public int timeoutSeconds;

        /// <summary>Global max response tokens requested per generation.</summary>
        public int maxTokens;

        /// <summary>Global cap on concurrent in-flight requests across lanes.</summary>
        public int maxConcurrentRequests;

        /// <summary>Total number of configured lanes (including disabled / half-configured rows).</summary>
        public int laneCount;

        /// <summary>Number of lanes that actually participate in generation (enabled + url + model).</summary>
        public int activeLaneCount;

        /// <summary>True when the player opted into sharing raw API keys with other mods. When false,
        /// each lane's apiKey is empty (hasApiKey still reports whether a key is set). Default is false.</summary>
        public bool keySharingEnabled;

        /// <summary>The configured lanes, in the player's list order (also the failover order). Never
        /// null; an independent copy, so mutating game state after the read does not change it.</summary>
        public List<DiaryApiLaneSnapshot> lanes = new List<DiaryApiLaneSnapshot>();
    }
}
