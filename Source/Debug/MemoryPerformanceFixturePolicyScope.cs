// MemoryPerformanceFixturePolicyScope.cs — the friend-only, non-Scribed production-policy override
// scope for benchmark/RimTest fixtures (design/MEMORY_SYSTEM_IMPLEMENTATION_PLAN.md §T17.2,
// policy-injection version "memory-benchmark-policy-override-v1", WP-RIMTEST).
//
// While active, fixture code may observe/override effective memory policy values WITHOUT touching
// the player's settings file and WITHOUT any Scribe representation: this scope is transient,
// process-local, and never saved. Production code must treat Active exactly like a settings read
// (main thread, fail-closed default false). The M0 XML vector parity stays owned by the tuning Def.
//
// New to C#? `using` blocks dispose the scope, so an exception can never leave the override stuck.
using System;
using System.Collections.Generic;

namespace PawnDiary
{
    internal static class MemoryPerformanceFixturePolicy
    {
        private static Dictionary<string, string> capacityVector;
        private static MemoryPolicySnapshot effectivePolicy;

        /// <summary>True only while a fixture scope is on the call stack.</summary>
        public static bool Active { get; internal set; }

        /// <summary>Fixture tag recorded by the scope for diagnostics; never persisted.</summary>
        public static string ScopeTag { get; internal set; }

        /// <summary>The authenticated settings publication installed for the current cell.</summary>
        public static MemoryPolicySnapshot EffectivePolicy => Active ? effectivePolicy : null;

        /// <summary>
        /// Returns one raw vector coordinate only while a fixture cell is active. A missing
        /// coordinate deliberately falls through to the committed XML value.
        /// </summary>
        public static bool TryReadCapacityEncoding(string name, out string valueEncoding)
        {
            valueEncoding = string.Empty;
            return Active
                && capacityVector != null
                && !string.IsNullOrWhiteSpace(name)
                && capacityVector.TryGetValue(name, out valueEncoding);
        }

        internal static void Install(
            string scopeTag,
            IDictionary<string, string> vector,
            MemoryPolicySnapshot policy)
        {
            if (Active)
                throw new InvalidOperationException(
                    "MemoryPerformanceFixturePolicyScope is not reentrant.");

            Dictionary<string, string> detached = null;
            if (vector != null)
            {
                detached = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (KeyValuePair<string, string> row in vector)
                {
                    if (string.IsNullOrWhiteSpace(row.Key) || row.Value == null
                        || detached.ContainsKey(row.Key))
                        throw new ArgumentException(
                            "The performance capacity vector is not canonical.", "vector");
                    detached.Add(row.Key, row.Value);
                }
            }

            capacityVector = detached;
            effectivePolicy = policy;
            ScopeTag = scopeTag ?? string.Empty;
            Active = true;
        }

        internal static void Clear()
        {
            Active = false;
            ScopeTag = string.Empty;
            capacityVector = null;
            effectivePolicy = null;
        }
    }

    /// <summary>Disposable RAII-style scope; nested or overlapping scopes are rejected so a leaked
    /// override cannot silently change production behavior.</summary>
    internal sealed class MemoryPerformanceFixturePolicyScope : IDisposable
    {
        private bool disposed;

        public MemoryPerformanceFixturePolicyScope(string scopeTag)
            : this(scopeTag, null, null)
        {
        }

        /// <summary>
        /// Installs a detached raw capacity vector and immutable settings publication. Manifest
        /// authentication stays in the friend RimTest adapter; production only receives the
        /// already-validated cell policy and applies its ordinary parsing and defensive ceilings.
        /// </summary>
        public MemoryPerformanceFixturePolicyScope(
            string scopeTag,
            IDictionary<string, string> capacityVector,
            MemoryPolicySnapshot effectivePolicy)
        {
            MemoryPerformanceFixturePolicy.Install(
                scopeTag, capacityVector, effectivePolicy);
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            MemoryPerformanceFixturePolicy.Clear();
        }
    }
}
