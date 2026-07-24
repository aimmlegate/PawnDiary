// Central, Verse-free registry of every Harmony hook registration outcome. Patch registrars report
// each hook here as they run, and DiaryModStartup logs one summary line at the end of startup, so a
// RimWorld game update that breaks hooks is visible at a glance instead of scattered across
// individual per-hook warnings. Reporting-only: registrars keep their own warnings, fallbacks, and
// *HookReady flags — nothing here changes what gets patched.
//
// This file deliberately references no Verse/RimWorld/Harmony types. DiaryModStartup owns the
// actual Log.Message/Log.Warning calls, which keeps this class compilable by the pure
// DiaryPatchManifestTests console suite (see tests/) without game assemblies.
// New to C#/RimWorld? See AGENTS.md ("Harmony patches").
using System.Collections.Generic;
using System.Text;

namespace PawnDiary
{
    /// <summary>
    /// Collects the outcome of every Harmony patch registration (attribute-discovered and fragile)
    /// during startup, then formats the one-line health summary and the degraded-hook detail list.
    /// The loaded RimTest canary asserts a fully healthy manifest, so after a game update one test
    /// run names every broken hook.
    /// </summary>
    internal static class DiaryPatchManifest
    {
        /// <summary>Outcome of one hook registration attempt.</summary>
        internal enum HookStatus
        {
            /// <summary>The hook was installed on its target.</summary>
            Applied,

            /// <summary>
            /// The target method was missing or changed shape; the hook's named fallback (or its
            /// graceful feature disable) is active. The mod keeps running — a diary feature is
            /// reduced. This is the expected signature after a RimWorld update renames a target.
            /// </summary>
            Degraded,

            /// <summary>Registration threw unexpectedly (beyond a merely missing target).</summary>
            Failed,

            /// <summary>
            /// Registration intentionally did not apply on this setup — a DLC or optional mod is
            /// inactive, or an optional enhancement target is absent by design. Not an error.
            /// </summary>
            Skipped
        }

        /// <summary>One recorded registration outcome. Immutable after construction.</summary>
        internal sealed class Entry
        {
            /// <summary>Feature group the hook belongs to ("Royalty", "Biotech", "attribute").</summary>
            public readonly string area;

            /// <summary>The vanilla target the hook attaches to, in human-readable form.</summary>
            public readonly string target;

            /// <summary>How registration ended.</summary>
            public readonly HookStatus status;

            /// <summary>Optional context: failure reason, active fallback, or skip cause.</summary>
            public readonly string detail;

            public Entry(string area, string target, HookStatus status, string detail)
            {
                this.area = area ?? string.Empty;
                this.target = target ?? string.Empty;
                this.status = status;
                this.detail = detail ?? string.Empty;
            }
        }

        // Defensive caps so a pathological exception message can't balloon the startup log line.
        // These are formatting limits, not feature policy, so hardcoding them is intentional.
        private const int MaximumDetailCharacters = 240;
        private const int MaximumDetailListCharacters = 4000;

        // Registration normally happens once, on RimWorld's startup static-constructor thread, but
        // the lock keeps snapshot reads from tests or future off-thread registrars safe regardless.
        private static readonly object sync = new object();
        private static readonly List<Entry> entries = new List<Entry>();

        /// <summary>Clears all recorded outcomes. Startup calls this once; tests call it per case.</summary>
        public static void Reset()
        {
            lock (sync)
            {
                entries.Clear();
            }
        }

        /// <summary>
        /// Records one registration outcome. Call exactly once per hook (or per meaningful hook
        /// group when a registrar is all-or-nothing), right where the registrar already decides
        /// success, fallback, or skip.
        /// </summary>
        public static void Report(string area, string target, HookStatus status, string detail = null)
        {
            Entry entry = new Entry(area, target, status, Limit(detail, MaximumDetailCharacters));
            lock (sync)
            {
                entries.Add(entry);
            }
        }

        /// <summary>Returns a point-in-time copy of every recorded outcome.</summary>
        public static List<Entry> Snapshot()
        {
            lock (sync)
            {
                return new List<Entry>(entries);
            }
        }

        /// <summary>Counts recorded outcomes with the given status.</summary>
        public static int Count(HookStatus status)
        {
            int count = 0;
            lock (sync)
            {
                for (int i = 0; i < entries.Count; i++)
                {
                    if (entries[i].status == status) count++;
                }
            }

            return count;
        }

        /// <summary>True when nothing registered as Degraded or Failed — the update-day green light.</summary>
        public static bool AllHealthy()
        {
            return Count(HookStatus.Degraded) == 0 && Count(HookStatus.Failed) == 0;
        }

        /// <summary>
        /// One-line health summary for the startup log, e.g.
        /// <c>Hooks: 38 applied, 0 degraded, 0 failed, 2 skipped.</c>
        /// </summary>
        public static string BuildSummary()
        {
            return "Hooks: " + Count(HookStatus.Applied) + " applied, "
                + Count(HookStatus.Degraded) + " degraded, "
                + Count(HookStatus.Failed) + " failed, "
                + Count(HookStatus.Skipped) + " skipped.";
        }

        /// <summary>
        /// Semicolon-separated list of every Degraded/Failed entry with its detail, or an empty
        /// string when the manifest is healthy. Startup logs this as a warning only when non-empty.
        /// </summary>
        public static string BuildDetail()
        {
            StringBuilder builder = new StringBuilder();
            List<Entry> snapshot = Snapshot();
            for (int i = 0; i < snapshot.Count; i++)
            {
                Entry entry = snapshot[i];
                if (entry.status != HookStatus.Degraded && entry.status != HookStatus.Failed)
                {
                    continue;
                }

                if (builder.Length > 0) builder.Append("; ");
                builder.Append(entry.area).Append(' ').Append(entry.target)
                    .Append(" — ").Append(entry.status == HookStatus.Failed ? "failed" : "degraded");
                if (entry.detail.Length > 0)
                {
                    builder.Append(" (").Append(entry.detail).Append(')');
                }
            }

            return Limit(builder.ToString(), MaximumDetailListCharacters);
        }

        /// <summary>Trims a detail string to the cap, marking the cut with an ellipsis.</summary>
        private static string Limit(string value, int maximumCharacters)
        {
            string clean = (value ?? string.Empty).Trim();
            if (clean.Length <= maximumCharacters) return clean;
            return clean.Substring(0, maximumCharacters - 3) + "...";
        }
    }
}
