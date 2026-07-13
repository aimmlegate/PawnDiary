// Process-global owner for integration capture-capability health. The public facade writes through
// this class, while XML group availability reads it. The pure registry underneath is locked because
// RimWorld constructs Mod classes on a loading thread but event classification runs on the game thread.
//
// New to C#/RimWorld? See AGENTS.md.
using System.Collections.Generic;

namespace PawnDiary.Integration
{
    /// <summary>Bridges the public integration API to XML compatibility-group availability gates.</summary>
    internal static class CaptureCapabilities
    {
        // Defensive process-global cap. Stable first-party adapters use only a handful of ids; this
        // prevents a buggy third-party adapter from growing the registry with churning identifiers.
        private const int MaxCapabilities = 64;

        private static readonly CaptureCapabilityRegistry Registry =
            new CaptureCapabilityRegistry(MaxCapabilities);

        /// <summary>Marks one stable capability id ready or unavailable.</summary>
        public static bool SetReady(string id, bool ready)
        {
            return Registry.SetReady(id, ready);
        }

        /// <summary>True when one stable capability id is currently ready.</summary>
        public static bool IsReady(string id)
        {
            return Registry.IsReady(id);
        }

        /// <summary>True when any id in an XML-authored gate is currently ready.</summary>
        public static bool AnyReady(IEnumerable<string> ids)
        {
            return Registry.AnyReady(ids);
        }
    }
}
