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

namespace PawnDiary
{
    internal static class MemoryPerformanceFixturePolicy
    {
        /// <summary>True only while a fixture scope is on the call stack.</summary>
        public static bool Active { get; internal set; }

        /// <summary>Fixture tag recorded by the scope for diagnostics; never persisted.</summary>
        public static string ScopeTag { get; internal set; }
    }

    /// <summary>Disposable RAII-style scope; nested or overlapping scopes are rejected so a leaked
    /// override cannot silently change production behavior.</summary>
    internal sealed class MemoryPerformanceFixturePolicyScope : IDisposable
    {
        private bool disposed;

        public MemoryPerformanceFixturePolicyScope(string scopeTag)
        {
            if (MemoryPerformanceFixturePolicy.Active)
            {
                throw new System.InvalidOperationException(
                    "MemoryPerformanceFixturePolicyScope is not reentrant.");
            }

            MemoryPerformanceFixturePolicy.Active = true;
            MemoryPerformanceFixturePolicy.ScopeTag = scopeTag ?? string.Empty;
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            MemoryPerformanceFixturePolicy.Active = false;
            MemoryPerformanceFixturePolicy.ScopeTag = string.Empty;
        }
    }
}
