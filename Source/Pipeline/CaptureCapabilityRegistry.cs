// Pure, thread-safe readiness registry for optional integration capture hooks. Runtime adapters
// publish stable capability ids after their fragile external hook is installed; XML compatibility
// groups can then suppress a lower-fidelity fallback only while that richer path is actually ready.
//
// This class names no RimWorld types so its replacement, cap, and lookup behavior can be exercised
// by the standalone test project.
//
// New to C#/RimWorld? See AGENTS.md.
using System;
using System.Collections.Generic;

namespace PawnDiary
{
    /// <summary>
    /// Stores the process-wide ready/not-ready state of stable integration capture capabilities.
    /// </summary>
    internal sealed class CaptureCapabilityRegistry
    {
        private readonly object sync = new object();
        private readonly HashSet<string> readyIds =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly int maxCapabilities;

        /// <summary>Creates a registry with a defensive maximum number of simultaneously ready ids.</summary>
        public CaptureCapabilityRegistry(int maxCapabilities)
        {
            this.maxCapabilities = maxCapabilities;
        }

        /// <summary>
        /// Marks one capability ready or not ready. Blank ids are rejected; clearing an absent id is
        /// still accepted so bridge cleanup can stay idempotent.
        /// </summary>
        public bool SetReady(string id, bool ready)
        {
            string normalizedId = NormalizeId(id);
            if (string.IsNullOrEmpty(normalizedId))
            {
                return false;
            }

            lock (sync)
            {
                if (!ready)
                {
                    readyIds.Remove(normalizedId);
                    return true;
                }

                if (readyIds.Contains(normalizedId))
                {
                    return true;
                }

                if (maxCapabilities > 0 && readyIds.Count >= maxCapabilities)
                {
                    return false;
                }

                readyIds.Add(normalizedId);
                return true;
            }
        }

        /// <summary>True when the given non-blank capability id is currently ready.</summary>
        public bool IsReady(string id)
        {
            string normalizedId = NormalizeId(id);
            if (string.IsNullOrEmpty(normalizedId))
            {
                return false;
            }

            lock (sync)
            {
                return readyIds.Contains(normalizedId);
            }
        }

        /// <summary>True when at least one listed capability id is currently ready.</summary>
        public bool AnyReady(IEnumerable<string> ids)
        {
            if (ids == null)
            {
                return false;
            }

            lock (sync)
            {
                foreach (string id in ids)
                {
                    string normalizedId = NormalizeId(id);
                    if (!string.IsNullOrEmpty(normalizedId) && readyIds.Contains(normalizedId))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static string NormalizeId(string id)
        {
            return string.IsNullOrWhiteSpace(id) ? string.Empty : id.Trim();
        }
    }
}
